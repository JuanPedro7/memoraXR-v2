using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SocraticTutor.Core;
using UnityEngine.Networking;

namespace SocraticTutor.AI
{
    /// <summary>
    /// Production implementation of <see cref="IAiProvider"/> targeting the Google Gemini REST API.
    /// Operates directly via <see cref="UnityWebRequest"/> to preserve full AOT/IL2CPP compatibility on Meta Quest 3.
    /// Implements Bring-Your-Own-Key (BYOK) architecture, exponential backoff retries, and markdown block stripping.
    /// </summary>
    public sealed class GeminiAiProvider : IAiProvider
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
        private const int MaxQuotaRetries = 3;
        private const int BaseWaitSeconds = 2;
        private const int RequestTimeoutSeconds = 60;
        private const string ParsingErrorPrefix = "[parsing_error]";

        private const string PromptTemplate =
            "You are an academic tutor assistant that creates high-retention study flashcards from textbook notes. " +
            "Analyze the following excerpt and produce between 1 and 5 targeted study cards capturing the core principles. " +
            "Each card must contain a concise 'front' (self-contained conceptual question) and 'back' (clear, direct explanation).\n\n" +
            "MATHEMATICAL NOTATION:\n" +
            "All formulas, variables, and mathematical expressions must be written in standard LaTeX enclosed in single dollar signs '$' " +
            "for inline math (e.g. $E = mc^2$) or double dollar signs '$$' for display blocks. Never output raw Unicode math symbols.\n\n" +
            "JSON ESCAPING:\n" +
            "Because LaTeX uses backslashes (\\), ensure all backslashes are properly escaped in the JSON string (\\\\frac, \\\\Delta).\n" +
            "Respond ONLY with a valid JSON array matching this exact schema: [{\"front\": \"...\", \"back\": \"...\"}]\n\n" +
            "Excerpt:\n{{text}}";

        private const string TextPlaceholder = "{{text}}";

        private readonly string _apiKey;
        private readonly string _modelName;

        public GeminiAiProvider(string apiKey, string modelName = "gemini-1.5-flash")
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("AI API key must not be null or empty.", nameof(apiKey));
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new ArgumentException("Model name must not be null or empty.", nameof(modelName));
            }

            _apiKey = apiKey;
            _modelName = modelName;
        }

        public int RecommendedChunkSize => 2000;

        public async Task<Result<IReadOnlyList<CardCandidate>>> GenerateCandidatesAsync(
            string textChunk, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(textChunk))
            {
                return Result<IReadOnlyList<CardCandidate>>.Error("Input text chunk is empty.");
            }

            var prompt = PromptTemplate.Replace(TextPlaceholder, textChunk);
            var initialAttempt = await SendAndParseAsync(prompt, cancellationToken);
            if (initialAttempt.IsSuccess)
            {
                return initialAttempt;
            }

            // If the failure was due to malformed JSON, issue a targeted single repair turn.
            if (!initialAttempt.ErrorMessage.StartsWith(ParsingErrorPrefix, StringComparison.Ordinal))
            {
                return initialAttempt;
            }

            var repairPrompt = prompt + "\n\nNote: Your previous output was not a valid JSON array according to the specified schema. Output strictly the JSON array.";
            return await SendAndParseAsync(repairPrompt, cancellationToken);
        }

        private async Task<Result<IReadOnlyList<CardCandidate>>> SendAndParseAsync(
            string prompt, CancellationToken cancellationToken)
        {
            var rawResult = await ExecuteWithRetriesAsync(prompt, cancellationToken);
            if (!rawResult.IsSuccess)
            {
                return Result<IReadOnlyList<CardCandidate>>.Error(rawResult.ErrorMessage);
            }

            GeminiResponseDto responseDto;
            try
            {
                responseDto = JsonConvert.DeserializeObject<GeminiResponseDto>(rawResult.Value);
            }
            catch (JsonException ex)
            {
                return Result<IReadOnlyList<CardCandidate>>.Error($"{ParsingErrorPrefix} Unable to deserialize API envelope: {ex.Message}");
            }

            var generatedText = responseDto?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(generatedText))
            {
                return Result<IReadOnlyList<CardCandidate>>.Error($"{ParsingErrorPrefix} No generated content returned by the model.");
            }

            List<GeneratedCardCandidateDto> candidatesDto;
            try
            {
                var cleanedJson = StripMarkdownCodeBlocks(generatedText);
                candidatesDto = JsonConvert.DeserializeObject<List<GeneratedCardCandidateDto>>(cleanedJson);
            }
            catch (JsonException ex)
            {
                return Result<IReadOnlyList<CardCandidate>>.Error($"{ParsingErrorPrefix} Model payload could not be parsed as card array: {ex.Message}");
            }

            if (candidatesDto == null)
            {
                return Result<IReadOnlyList<CardCandidate>>.Error($"{ParsingErrorPrefix} Deserialized candidate payload is null.");
            }

            var validCandidates = candidatesDto
                .Where(c => !string.IsNullOrWhiteSpace(c.Front) && !string.IsNullOrWhiteSpace(c.Back))
                .Select(c => new CardCandidate(c.Front, c.Back))
                .ToList();

            return Result<IReadOnlyList<CardCandidate>>.Ok(validCandidates);
        }

        public static string StripMarkdownCodeBlocks(string rawText)
        {
            var trimmed = rawText.Trim();
            if (trimmed.StartsWith("```"))
            {
                var firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    trimmed = trimmed.Substring(firstNewline + 1);
                }

                var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence >= 0)
                {
                    trimmed = trimmed.Substring(0, closingFence);
                }
            }

            return trimmed.Trim();
        }

        private async Task<Result<string>> ExecuteWithRetriesAsync(string prompt, CancellationToken cancellationToken)
        {
            var requestBody = new GeminiRequestDto
            {
                Contents = new List<GeminiContentDto>
                {
                    new GeminiContentDto
                    {
                        Role = "user",
                        Parts = new List<GeminiPartDto> { new GeminiPartDto { Text = prompt } }
                    }
                },
                GenerationConfig = new GeminiGenerationConfigDto { ResponseMimeType = "application/json" }
            };

            var payloadBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestBody));
            var endpointUrl = $"{BaseUrl}{Uri.EscapeDataString(_modelName)}:generateContent?key={Uri.EscapeDataString(_apiKey)}";

            for (var attempt = 1; attempt <= MaxQuotaRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Result<string>.Error("Request canceled.");
                }

                using var webRequest = new UnityWebRequest(endpointUrl, "POST")
                {
                    uploadHandler = new UploadHandlerRaw(payloadBytes),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = RequestTimeoutSeconds
                };
                webRequest.uploadHandler.contentType = "application/json";
                webRequest.SetRequestHeader("Content-Type", "application/json");

                await AwaitWebRequestAsync(webRequest, cancellationToken);

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    return Result<string>.Ok(webRequest.downloadHandler.text);
                }

                var statusCode = webRequest.responseCode;

                if (webRequest.result == UnityWebRequest.Result.ConnectionError)
                {
                    return Result<string>.Error("Network connection failure. Please verify Quest internet connectivity.");
                }

                if (statusCode == 401 || statusCode == 403)
                {
                    return Result<string>.Error("Invalid or unauthorized API key.");
                }

                if (statusCode == 429 || statusCode >= 500)
                {
                    if (attempt < MaxQuotaRetries)
                    {
                        var backoffDelay = TimeSpan.FromSeconds(Math.Pow(BaseWaitSeconds, attempt));
                        await Task.Delay(backoffDelay, cancellationToken);
                        continue;
                    }

                    return Result<string>.Error(statusCode == 429
                        ? "API rate limit reached. Please retry shortly."
                        : $"Service temporarily unavailable (HTTP {statusCode}).");
                }

                return Result<string>.Error($"Gemini request rejected with HTTP {statusCode}: {webRequest.error}");
            }

            return Result<string>.Error("Max request retry threshold exceeded.");
        }

        private static async Task AwaitWebRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ => tcs.TrySetResult(true);

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                await tcs.Task;
            }
        }
    }
}
