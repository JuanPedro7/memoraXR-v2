using System;
using System.Collections.Generic;
using System.Text;

namespace SocraticTutor.Core
{
    /// <summary>
    /// Buffers token-by-token streaming LLM outputs and extracts complete grammatical sentences.
    /// Synthesizing speech token-by-token sounds unnatural and overburdens the TTS engine.
    /// By partitioning the incoming stream on terminal punctuation marks ('.', '?', '!'),
    /// speech synthesis can play early sentences while the LLM continues generating later ones.
    /// </summary>
    public sealed class SentenceAccumulator
    {
        private static readonly char[] SentenceTerminators = { '.', '?', '!' };
        private readonly StringBuilder _buffer = new StringBuilder();

        /// <summary>
        /// Ingests a streaming token/fragment and returns all complete sentences closed by this addition.
        /// Any trailing incomplete sentence remains buffered for subsequent chunks.
        /// </summary>
        public IReadOnlyList<string> AppendFragment(string fragment)
        {
            var completedSentences = new List<string>();
            if (string.IsNullOrEmpty(fragment))
            {
                return completedSentences;
            }

            _buffer.Append(fragment);

            var currentSentenceStart = 0;
            for (var i = 0; i < _buffer.Length; i++)
            {
                if (Array.IndexOf(SentenceTerminators, _buffer[i]) < 0)
                {
                    continue;
                }

                var sentence = _buffer.ToString(currentSentenceStart, i - currentSentenceStart + 1).Trim();
                if (sentence.Length > 0)
                {
                    completedSentences.Add(sentence);
                }

                currentSentenceStart = i + 1;
            }

            if (currentSentenceStart > 0)
            {
                var remaining = _buffer.ToString(currentSentenceStart, _buffer.Length - currentSentenceStart);
                _buffer.Clear();
                _buffer.Append(remaining);
            }

            return completedSentences;
        }

        /// <summary>
        /// Flushes any remaining trailing text when the LLM stream terminates, ensuring that ideas
        /// lacking a trailing punctuation mark are not dropped.
        /// </summary>
        public string FlushRemaining()
        {
            var remaining = _buffer.ToString().Trim();
            _buffer.Clear();
            return remaining;
        }
    }
}
