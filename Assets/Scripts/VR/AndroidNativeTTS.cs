using System;
using System.Threading.Tasks;
using SocraticTutor.Core;
using UnityEngine;

namespace SocraticTutor.VR
{
    /// <summary>
    /// Low-level text-to-speech engine utilizing Android AOSP android.speech.tts.TextToSpeech via JNI.
    /// Operates dependency-free without third-party plugins on Meta Quest 3 (Horizon OS).
    /// </summary>
    public static class AndroidNativeTTS
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int InitSuccess = 0; // TextToSpeech.SUCCESS
        private const int QueueAdd = 1;    // TextToSpeech.QUEUE_ADD

        private static AndroidJavaObject _ttsInstance;
        private static Task<Result<bool>> _inFlightInitialization;
#endif

        /// <summary>
        /// Ensures the native Android TTS service is initialized and configured for speech synthesis.
        /// Returns safely on non-Android platforms (Editor / Standalone).
        /// </summary>
        public static Task<Result<bool>> EnsureInitializedAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_ttsInstance != null)
            {
                return Task.FromResult(Result<bool>.Ok(true));
            }

            if (_inFlightInitialization != null)
            {
                return _inFlightInitialization;
            }

            _inFlightInitialization = InitializeInternalAsync();
            return _inFlightInitialization;
#else
            return Task.FromResult(Result<bool>.Ok(true));
#endif
        }

        /// <summary>
        /// Enqueues a sentence into the native playback queue.
        /// </summary>
        public static void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (_ttsInstance == null)
            {
                Debug.LogWarning($"[AndroidNativeTTS] Speak invoked before initialization: {text}");
                return;
            }

            try
            {
                _ttsInstance.Call<int>("speak", text, QueueAdd, null, "socratic-tts");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidNativeTTS] Synthesis call failed: {ex.Message}");
            }
#else
            Debug.Log($"[AndroidNativeTTS (Editor Mock)] {text}");
#endif
        }

        /// <summary>
        /// Immediately halts any currently playing utterance and clears queued audio.
        /// </summary>
        public static void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_ttsInstance == null)
            {
                return;
            }

            try
            {
                _ttsInstance.Call<int>("stop");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidNativeTTS] Stop invocation failed: {ex.Message}");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<Result<bool>> InitializeInternalAsync()
        {
            var tcs = new TaskCompletionSource<int>();

            try
            {
                var currentActivity = UnityEngine.Android.AndroidApplication.currentActivity;
                if (currentActivity == null)
                {
                    _inFlightInitialization = null;
                    return Result<bool>.Error("Android host activity unavailable.");
                }

                var listener = new TtsInitListener(status => tcs.TrySetResult(status));
                _ttsInstance = new AndroidJavaObject("android.speech.tts.TextToSpeech", currentActivity, listener);
            }
            catch (Exception ex)
            {
                _ttsInstance = null;
                _inFlightInitialization = null;
                return Result<bool>.Error($"Failed instantiating TextToSpeech object: {ex.Message}");
            }

            var status = await tcs.Task;
            if (status != InitSuccess)
            {
                _ttsInstance = null;
                _inFlightInitialization = null;
                return Result<bool>.Error("Android TextToSpeech engine initialization failed.");
            }

            try
            {
                using var locale = new AndroidJavaObject("java.util.Locale", "en");
                var langResult = _ttsInstance.Call<int>("setLanguage", locale);
                if (langResult < 0)
                {
                    Debug.LogWarning($"[AndroidNativeTTS] Selected locale returned code {langResult}. Falling back to default.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AndroidNativeTTS] Non-fatal locale assignment error: {ex.Message}");
            }

            return Result<bool>.Ok(true);
        }

        private sealed class TtsInitListener : AndroidJavaProxy
        {
            private readonly Action<int> _onInitCallback;

            internal TtsInitListener(Action<int> onInitCallback)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _onInitCallback = onInitCallback;
            }

            void onInit(int status)
            {
                _onInitCallback?.Invoke(status);
            }
        }
#endif
    }
}
