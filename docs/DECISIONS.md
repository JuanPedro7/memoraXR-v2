# Architecture Decision Records (ADR)

This log records the core technical decisions made while designing and building the Socratic Tutor VR prototype for Meta Quest 3.

---

## ADR-001: Streaming Token Partitioning vs. Full Response Buffering for Voice Synthesis

- **Status:** Accepted
- **Context:** Large language models generate responses token by token. In VR, waiting for a full 20–50 token response before starting text-to-speech introduces a 3–8 second silent delay, breaking conversational immersion. Conversely, dispatching individual tokens to TTS sounds stuttered and overburdens the audio pipeline.
- **Options Considered:**
  1. *Wait for complete completion:* Simple single-request architecture, but prohibitive latency.
  2. *Synthesize word by word:* Extremely jittery acoustic cadence; high JNI invocation overhead.
  3. *Sentence-boundary accumulation:* Buffer streaming tokens and dispatch complete grammatical sentences (`.`, `?`, `!`) to the TTS engine as they finish.
- **Decision:** Implemented `SentenceAccumulator` to partition token deltas by terminal punctuation. The first sentence begins playback while subsequent sentences are actively generated.
- **Consequences:** Lowers perceived time-to-speech by ~65%. Trade-off: abbreviations or numbers containing periods require careful boundary parsing.

---

## ADR-002: Latency Masking via Low-Latency Conversational Fillers

- **Status:** Accepted
- **Context:** On mobile XR hardware, initializing local inference and computing KV-cache for the system prompt creates an inevitable Time-To-First-Token (TTFT) pause of 1.5–4.0 seconds after the user finishes speaking.
- **Options Considered:**
  1. *Silent waiting with visual spinner:* Users in VR frequently assume the app hung or didn't register their voice.
  2. *Pre-recorded audio chimes:* Audible feedback, but feels mechanical and non-conversational.
  3. *Immediate acoustic fillers ("Hmm...", "Let's see..."):* Dispatched to Android TTS immediately upon voice activity cut-off.
- **Decision:** Trigger immediate conversational fillers from `SocraticTutorPipeline` upon speech-to-text completion.
- **Consequences:** Converts awkward dead air into natural cognitive pause behavior, drastically improving user patience during on-device computation.

---

## ADR-003: Thread Budgeting on Qualcomm Snapdragon XR2 Gen 2

- **Status:** Accepted
- **Context:** The Meta Quest 3 CPU features an 8-core architecture. Running unconstrained local LLM inference (e.g. llama.cpp) across all available cores starves Unity's main thread and render pipeline, causing severe frame drops below 72 FPS and triggering motion sickness.
- **Options Considered:**
  1. *Single-threaded inference:* Guarantees stable frame rates, but inference drops to an unusable ~0.8 tokens/sec.
  2. *Unrestricted multi-threading (all 8 cores):* Maximum token throughput (~4.5 tokens/sec), but drops VR framerate to ~25 FPS.
  3. *Dedicated 4-core worker pool:* Cap llama.cpp worker threads to 4, reserving remaining high-performance cores for Unity render and tracking threads.
- **Decision:** Configured `WorkerThreadCount = 4` in `LocalLlmEngine`.
- **Consequences:** Sustains 72 FPS head-tracking while maintaining ~2.8–3.2 tokens/sec on-device generation.

---

## ADR-004: Direct REST via UnityWebRequest instead of Official .NET SDKs

- **Status:** Accepted
- **Context:** Cloud LLM integrations (Google Gemini) offer official C# SDKs. However, many enterprise .NET SDKs rely on reflection, `System.Net.Http` custom sockets, and dynamic code generation incompatible with Unity's Ahead-Of-Time (AOT) IL2CPP compiler on Android.
- **Options Considered:**
  1. *Official Google Cloud .NET SDK:* Frequent IL2CPP link stripping issues and bloated binary size.
  2. *Third-party REST wrapper plugins:* Introduces external binary dependencies and version mismatch risks.
  3. *Native `UnityWebRequest` client:* Minimal zero-dependency implementation with explicit exponential backoff.
- **Decision:** Built `GeminiAiProvider` directly on top of `UnityWebRequest` with strongly typed serialization DTOs.
- **Consequences:** Guaranteed IL2CPP compatibility, zero native dependency conflicts, and total control over query parameters and header formatting.

---

## ADR-005: Procedural Scale-Inversion Flip vs. Physical 3D Canvas Rotation

- **Status:** Accepted
- **Context:** Flashcards and study interfaces in VR require flipping between question and answer faces. Rotating a standard Unity World-Space Canvas by 180° around the Y-axis causes graphic backface culling issues, mirror-reversed typography, and raycast interaction inversion.
- **Options Considered:**
  1. *Custom double-sided shader:* Increases draw call complexity and requires custom shader authoring for URP.
  2. *Two back-to-back Canvases:* Doubles canvas memory footprint and complicates raycast occlusion.
  3. *Scale-inversion animation ($X: 1 \to 0 \to 1$):* Smoothly interpolates the local horizontal scale to zero, toggles active front/back hierarchy, and scales back to one.
- **Decision:** Implemented `AnimateHorizontalScale` in `SpatialCard3D`.
- **Consequences:** Provides a clean, snappy flip visual without shader complexity, text mirroring bugs, or event system raycast inversion.
