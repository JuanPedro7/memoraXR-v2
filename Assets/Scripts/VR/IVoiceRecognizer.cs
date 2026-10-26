using System;
using System.Threading;
using System.Threading.Tasks;
using SocraticTutor.Core;

namespace SocraticTutor.VR
{
    /// <summary>
    /// Contract for on-device voice recognition (STT) on Meta Quest 3.
    /// Manages Android runtime audio permissions, audio capture lifecycle, and push-to-talk / hands-free VAD events.
    /// </summary>
    public interface IVoiceRecognizer : IDisposable
    {
        /// <summary>
        /// Validates and asynchronously requests Android RECORD_AUDIO permission if ungranted.
        /// </summary>
        Task<Result<bool>> EnsureMicrophonePermissionAsync();

        /// <summary>
        /// Begins audio stream recording from the Quest headset microphone array.
        /// </summary>
        void StartListening();

        /// <summary>
        /// Stops active recording, processes audio through the speech model, and returns the transcribed text.
        /// </summary>
        Task<Result<string>> StopAndTranscribeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Immediately cancels active listening without transcribing or dispatching turn completion events.
        /// </summary>
        void CancelListening();

        /// <summary>
        /// Emitted during streaming speech recognition with interim partial transcripts.
        /// </summary>
        event Action<string> OnPartialResult;

        /// <summary>
        /// Dispatched when voice activity detection (VAD) detects end of user utterance.
        /// </summary>
        event Action OnEndOfTurnDetected;
    }
}
