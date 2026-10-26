using System.Collections.Generic;
using Newtonsoft.Json;

namespace SocraticTutor.AI
{
    /// <summary>
    /// Data transfer objects mapped to the official Google Gemini REST API (:generateContent).
    /// Kept minimal to optimize serialization performance on mobile XR processors.
    /// </summary>
    public sealed class GeminiRequestDto
    {
        [JsonProperty("contents")]
        public List<GeminiContentDto> Contents { get; set; } = new List<GeminiContentDto>();

        [JsonProperty("generationConfig")]
        public GeminiGenerationConfigDto GenerationConfig { get; set; }
    }

    public sealed class GeminiContentDto
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("parts")]
        public List<GeminiPartDto> Parts { get; set; } = new List<GeminiPartDto>();
    }

    public sealed class GeminiPartDto
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public sealed class GeminiGenerationConfigDto
    {
        [JsonProperty("responseMimeType")]
        public string ResponseMimeType { get; set; }
    }

    public sealed class GeminiResponseDto
    {
        [JsonProperty("candidates")]
        public List<GeminiCandidateDto> Candidates { get; set; }
    }

    public sealed class GeminiCandidateDto
    {
        [JsonProperty("content")]
        public GeminiContentDto Content { get; set; }

        [JsonProperty("finishReason")]
        public string FinishReason { get; set; }
    }

    public sealed class GeneratedCardCandidateDto
    {
        [JsonProperty("front")]
        public string Front { get; set; }

        [JsonProperty("back")]
        public string Back { get; set; }
    }
}
