using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SocraticTutor.Core;

namespace SocraticTutor.AI
{
    /// <summary>
    /// Represents a study card candidate extracted by the AI provider from source material.
    /// </summary>
    public sealed class CardCandidate
    {
        public string Front { get; }
        public string Back { get; }
        public string SourceExcerpt { get; }

        public CardCandidate(string front, string back, string sourceExcerpt = null)
        {
            Front = front;
            Back = back;
            SourceExcerpt = sourceExcerpt;
        }
    }

    /// <summary>
    /// Abstraction contract for generative AI providers (cloud Gemini or local GGUF models).
    /// Decouples higher-level study orchestrators from networking, SDKs, or local model formats.
    /// </summary>
    public interface IAiProvider
    {
        /// <summary>
        /// Optimal chunk size (characters) recommended for this provider's model context window.
        /// Local quantized models typically operate with smaller context windows than cloud endpoints.
        /// </summary>
        int RecommendedChunkSize { get; }

        /// <summary>
        /// Generates flashcard candidates from a segment of source material.
        /// </summary>
        Task<Result<IReadOnlyList<CardCandidate>>> GenerateCandidatesAsync(
            string textChunk, CancellationToken cancellationToken = default);
    }
}
