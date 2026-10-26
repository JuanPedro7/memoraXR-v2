using System;
using System.Threading;
using System.Threading.Tasks;
using SocraticTutor.Core;
using SocraticTutor.VR;
using UnityEngine;

namespace SocraticTutor.AI
{
    /// <summary>
    /// Coordinates the conversational turn of the Socratic Tutor for Meta Quest 3:
    /// Speech-To-Text (<see cref="IVoiceRecognizer"/>) -> Immediate acoustic filler phrase ->
    /// Streaming on-device LLM inference (<see cref="LocalLlmEngine"/>) -> Sentence aggregation (<see cref="SentenceAccumulator"/>) ->
    /// Concurrent text-to-speech synthesis (<see cref="AndroidNativeTTS"/>).
    /// </summary>
    public sealed class SocraticTutorPipeline
    {
        internal delegate Task<Result<bool>> InitializeVoiceDelegate();
        internal delegate Task<Result<string>> GenerateStreamingResponseDelegate(
            string prompt, Action<string> onChunk, CancellationToken cancellationToken);
        internal delegate void SpeakDelegate(string text);
        internal delegate void StopVoiceDelegate();
        internal delegate Task<Result<bool>> PrewarmLlmDelegate();

        // Conversational fillers played immediately after user utterance to mask initial LLM time-to-first-token (TTFT).
        private static readonly string[] ConversationalFillers = { "Hmm...", "Let's see...", "Let me think about that..." };

        // Socratic tutoring prompt constrained for low-latency synthesis: max 2 sentences, strictly guiding questions, never raw solutions.
        private const string SystemPromptTutor =
            "You are a Socratic tutor. Be concise: maximum 2 sentences per response. " +
            "Never provide direct answers; ask targeted questions that guide the student to deduce the solution. " +
            "End each sentence with clear terminal punctuation (. ? !), as your response is synthesized to speech sentence-by-sentence in real time.";

        private const int MaxResponseTokens = 80;

        private readonly IVoiceRecognizer _voiceRecognizer;
        private readonly InitializeVoiceDelegate _initializeVoice;
        private readonly GenerateStreamingResponseDelegate _generateResponse;
        private readonly SpeakDelegate _speak;
        private readonly StopVoiceDelegate _stopVoice;
        private readonly PrewarmLlmDelegate _prewarmLlm;

        public SocraticTutorPipeline(IVoiceRecognizer voiceRecognizer, string ggufModelPath) : this(
            voiceRecognizer,
            AndroidNativeTTS.EnsureInitializedAsync,
            (prompt, onChunk, cancellationToken) => LocalLlmEngine.CompleteAsync(
                ggufModelPath, SystemPromptTutor, prompt, MaxResponseTokens, onChunk, cancellationToken),
            AndroidNativeTTS.Speak,
            AndroidNativeTTS.Stop,
            () => LocalLlmEngine.PrewarmAsync(ggufModelPath, SystemPromptTutor))
        {
        }

        internal SocraticTutorPipeline(
            IVoiceRecognizer voiceRecognizer,
            InitializeVoiceDelegate initializeVoice,
            GenerateStreamingResponseDelegate generateResponse,
            SpeakDelegate speak,
            StopVoiceDelegate stopVoice,
            PrewarmLlmDelegate prewarmLlm = null)
        {
            _voiceRecognizer = voiceRecognizer ?? throw new ArgumentNullException(nameof(voiceRecognizer));
            _initializeVoice = initializeVoice ?? throw new ArgumentNullException(nameof(initializeVoice));
            _generateResponse = generateResponse ?? throw new ArgumentNullException(nameof(generateResponse));
            _speak = speak ?? throw new ArgumentNullException(nameof(speak));
            _stopVoice = stopVoice ?? throw new ArgumentNullException(nameof(stopVoice));
            _prewarmLlm = prewarmLlm ?? (() => Task.FromResult(Result<bool>.Ok(true)));
        }

        /// <summary>
        /// Validates runtime mic permissions and warms up local model weights in the background.
        /// Non-blocking on TTS failures so visual interaction remains functional even without an audio engine.
        /// </summary>
        public async Task<Result<bool>> EnsureReadyAsync()
        {
            var micPermission = await _voiceRecognizer.EnsureMicrophonePermissionAsync();
            if (!micPermission.IsSuccess)
            {
                return micPermission;
            }

            var voiceInit = await _initializeVoice();
            if (!voiceInit.IsSuccess)
            {
                Debug.LogWarning($"[SocraticTutorPipeline] TTS initialization failed ({voiceInit.ErrorMessage}). Continuing with visual response only.");
            }

            // Warm up Qwen KV cache and system prompt concurrently during session startup.
            _ = PrewarmLlmInBackgroundAsync();

            return Result<bool>.Ok(true);
        }

        private async Task PrewarmLlmInBackgroundAsync()
        {
            try
            {
                var warmupResult = await _prewarmLlm();
                if (!warmupResult.IsSuccess)
                {
                    Debug.LogWarning($"[SocraticTutorPipeline] Model prewarm completed with warning: {warmupResult.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SocraticTutorPipeline] Unexpected error during model prewarm: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts capturing audio input. Automatically stops any ongoing speech output (barge-in support).
        /// </summary>
        public void StartListening()
        {
            StopVoice();
            _voiceRecognizer.StartListening();
        }

        /// <summary>
        /// Stops any currently playing audio utterance and flushes audio playback buffers.
        /// </summary>
        public void StopVoice() => _stopVoice();

        /// <summary>
        /// Cancels active listening turn without triggering transcript evaluation.
        /// </summary>
        public void CancelListening() => _voiceRecognizer.CancelListening();

        public event Action<string> OnPartialResult
        {
            add => _voiceRecognizer.OnPartialResult += value;
            remove => _voiceRecognizer.OnPartialResult -= value;
        }

        public event Action OnEndOfTurnDetected
        {
            add => _voiceRecognizer.OnEndOfTurnDetected += value;
            remove => _voiceRecognizer.OnEndOfTurnDetected -= value;
        }

        public void Dispose() => (_voiceRecognizer as IDisposable)?.Dispose();

        /// <summary>
        /// Completes a full conversational interaction: transcribes speech, emits low-latency acoustic filler,
        /// streams LLM tokens concurrently to UI and TTS pipeline, and returns the full aggregated response.
        /// </summary>
        public async Task<Result<string>> ConverseAsync(
            Action<string> onPhaseChanged = null,
            Action<string> onTokenDelta = null,
            CancellationToken cancellationToken = default)
        {
            onPhaseChanged?.Invoke("Transcribing...");
            var transcription = await _voiceRecognizer.StopAndTranscribeAsync(cancellationToken);
            if (!transcription.IsSuccess)
            {
                return Result<string>.Error(transcription.ErrorMessage);
            }

            if (string.IsNullOrWhiteSpace(transcription.Value))
            {
                return Result<string>.Error("No speech input detected.");
            }

            _speak(ConversationalFillers[UnityEngine.Random.Range(0, ConversationalFillers.Length)]);
            onPhaseChanged?.Invoke("Thinking...");

            var sentenceAccumulator = new SentenceAccumulator();
            var responseResult = await _generateResponse(transcription.Value, chunk =>
            {
                // Isolate UI exceptions to preserve the native LLM worker thread.
                try
                {
                    onTokenDelta?.Invoke(chunk);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SocraticTutorPipeline] UI delta handler failed: {ex.Message}");
                }

                foreach (var sentence in sentenceAccumulator.AppendFragment(chunk))
                {
                    _speak(sentence);
                }
            }, cancellationToken);

            if (!responseResult.IsSuccess)
            {
                return Result<string>.Error(responseResult.ErrorMessage);
            }

            var trailingText = sentenceAccumulator.FlushRemaining();
            if (!string.IsNullOrWhiteSpace(trailingText))
            {
                _speak(trailingText);
            }

            return responseResult;
        }
    }
}
