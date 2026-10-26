using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using SocraticTutor.Core;
using UnityEngine;

namespace SocraticTutor.AI
{
    /// <summary>
    /// Executes on-device local LLM inference on Meta Quest 3 via LLMUnity (LlamaLib/llama.cpp wrapper).
    /// Optimizes thread concurrency for Snapdragon XR2 Gen 2, normalizes accumulated streaming callbacks into deltas,
    /// and pre-warms KV-cache to reduce first-token latency during speech interactions.
    /// </summary>
    public sealed class LocalLlmEngine
    {
        private static readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan AgentInitTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ModelLoadTimeout = TimeSpan.FromMinutes(3);

        // Dedicates 4 cores to llama.cpp inference, leaving remaining cores for Unity's render thread to sustain 72 FPS.
        private const int WorkerThreadCount = 4;

        private static LLM _llm;
        private static LLMAgent _agent;
        private static string _loadedModelPath;

        /// <summary>
        /// Generates a streaming response for a given prompt using the local GGUF model.
        /// Serialized through a single semaphore to prevent memory exhaustion and GPU/NPU thrashing.
        /// </summary>
        public static async Task<Result<string>> CompleteAsync(
            string ggufModelPath,
            string systemPrompt,
            string prompt,
            int maxTokens,
            Action<string> onChunk = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ggufModelPath))
            {
                return Result<string>.Error("Model path must not be null or empty.");
            }

            await Lock.WaitAsync(cancellationToken);
            try
            {
                var initResult = await EnsureEngineAsync(ggufModelPath, cancellationToken);
                if (!initResult.IsSuccess)
                {
                    return Result<string>.Error(initResult.ErrorMessage);
                }

                _agent.systemPrompt = systemPrompt;
                _agent.numPredict = maxTokens;

                var stopwatch = Stopwatch.StartNew();
                var firstChunkLogged = false;

                Action<string> timedChunkCallback = onChunk == null ? null : chunk =>
                {
                    if (!firstChunkLogged)
                    {
                        firstChunkLogged = true;
                        Debug.Log($"[LocalLlmEngine] Time to first token: {stopwatch.Elapsed.TotalSeconds:F2}s");
                    }

                    onChunk(chunk);
                };

                var deltaCallback = timedChunkCallback == null ? null : CreateDeltaCallback(timedChunkCallback);

                string response;
                try
                {
                    response = await _agent.Chat(prompt, callback: deltaCallback, completionCallback: null, addToHistory: false);
                    Debug.Log($"[LocalLlmEngine] Inference completed in {stopwatch.Elapsed.TotalSeconds:F2}s ({response?.Length ?? 0} chars).");
                }
                catch (Exception ex)
                {
                    return Result<string>.Error($"Local inference failed: {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(response))
                {
                    return Result<string>.Error("Empty response returned by local engine.");
                }

                return Result<string>.Ok(response);
            }
            finally
            {
                Lock.Release();
            }
        }

        /// <summary>
        /// Pre-processes system prompt tokens to cache KV attention matrices prior to user input.
        /// </summary>
        public static async Task<Result<bool>> PrewarmAsync(
            string ggufModelPath, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ggufModelPath))
            {
                return Result<bool>.Error("Model path must not be null or empty.");
            }

            await Lock.WaitAsync(cancellationToken);
            try
            {
                var initResult = await EnsureEngineAsync(ggufModelPath, cancellationToken);
                if (!initResult.IsSuccess)
                {
                    return Result<bool>.Error(initResult.ErrorMessage);
                }

                _agent.systemPrompt = systemPrompt;

                try
                {
                    await _agent.Warmup();
                }
                catch (Exception ex)
                {
                    return Result<bool>.Error($"Failed to prewarm model weights: {ex.Message}");
                }

                return Result<bool>.Ok(true);
            }
            finally
            {
                Lock.Release();
            }
        }

        private static async Task<Result<bool>> EnsureEngineAsync(string ggufModelPath, CancellationToken cancellationToken)
        {
            if (_agent != null && _loadedModelPath == ggufModelPath)
            {
                return Result<bool>.Ok(true);
            }

            if (_llm != null && _loadedModelPath != ggufModelPath)
            {
                return Result<bool>.Error("Engine already initialized with a different model. Restart required to switch models.");
            }

            GameObject host = null;
            try
            {
                host = new GameObject("SocraticTutor_LocalLlmEngine");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.SetActive(false);

                _llm = host.AddComponent<LLM>();
                _llm.model = ggufModelPath;
                _llm.numThreads = WorkerThreadCount;

                _agent = host.AddComponent<LLMAgent>();
                _agent.llm = _llm;

                host.SetActive(true);

                await AwaitWithTimeoutAsync(_llm.WaitUntilReady(), ModelLoadTimeout, cancellationToken,
                    "Local engine model loading exceeded timeout threshold.");

                var agentTimer = Stopwatch.StartNew();
                while (_agent.llmAgent == null)
                {
                    if (agentTimer.Elapsed > AgentInitTimeout)
                    {
                        throw new TimeoutException("Native agent wrapper initialization timed out.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                if (host != null)
                {
                    UnityEngine.Object.Destroy(host);
                }

                _llm = null;
                _agent = null;
                _loadedModelPath = null;
                return Result<bool>.Error($"Failed to initialize local LLM engine: {ex.Message}");
            }

            _loadedModelPath = ggufModelPath;
            return Result<bool>.Ok(true);
        }

        /// <summary>
        /// Converts cumulative stream text snapshots into incremental token deltas.
        /// </summary>
        public static Action<string> CreateDeltaCallback(Action<string> onIncrementalChunk)
        {
            var previousSnapshot = string.Empty;
            return currentSnapshot =>
            {
                if (string.IsNullOrEmpty(currentSnapshot))
                {
                    return;
                }

                if (currentSnapshot.StartsWith(previousSnapshot, StringComparison.Ordinal))
                {
                    var delta = currentSnapshot.Substring(previousSnapshot.Length);
                    previousSnapshot = currentSnapshot;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        onIncrementalChunk(delta);
                    }
                }
                else
                {
                    previousSnapshot = currentSnapshot;
                    onIncrementalChunk(currentSnapshot);
                }
            };
        }

        private static async Task AwaitWithTimeoutAsync(
            Task task, TimeSpan timeout, CancellationToken cancellationToken, string timeoutMessage)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, linkedCts.Token));
            if (completedTask != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(timeoutMessage);
            }

            await task;
        }
    }
}
