# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-12 (Summer Prototype Release)

### Added
- **AI Tutoring Pipeline (`SocraticTutorPipeline`):**
  - Integrated speech-to-text, acoustic filler playback, streaming LLM inference, and sentence-level text-to-speech.
  - Barge-in voice interruption mechanism allowing the user to halt AI speech instantly.
  - Concurrent KV-cache pre-warming during user voice input.
- **On-Device Edge AI Engine (`LocalLlmEngine`):**
  - Thread-budgeted GGUF inference tuned specifically for Qualcomm Snapdragon XR2 Gen 2 (4 worker threads).
  - Snapshot-to-delta stream normalizer for real-time typewriter UI and acoustic synthesis.
  - Semaphore-guarded single-slot inference queue to prevent VR memory exhaustion.
- **Cloud AI Provider (`GeminiAiProvider`):**
  - Zero-dependency REST client over `UnityWebRequest` preserving AOT/IL2CPP compatibility.
  - Automatic exponential backoff retry for HTTP 429 and transient 5xx errors.
  - Single-turn JSON repair retry mechanism and markdown code block stripping.
- **Core Domain & Audio Stream Aggregation:**
  - `SentenceAccumulator` for sentence-boundary chunking (`.`, `?`, `!`) of streaming token output.
  - `Result<T>` functional outcome wrapper eliminating unhandled runtime exceptions.
- **Spatial UI & VR Interaction:**
  - `SpatialCard3D` procedural World-Space canvas with scale-inversion flip animation.
  - Debounced rating submissions preventing duplicate evaluation events in VR.
  - `XRInteractionInput` transforming Quest Touch controller tracking space poses into world rays.
  - `AndroidNativeTTS` low-level JNI bridge into Android `TextToSpeech` without third-party plugins.
