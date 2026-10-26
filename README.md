# Socratic Tutor VR

A voice-driven, low-latency AI study companion for Meta Quest 3 that teaches through guided inquiry rather than direct answers.

---

## 1. Demo / Status

This repository contains the clean source code from a prototype developed during the summer of 2026. 

Today, the core conversational and spatial interaction loop is fully implemented:
- Real-time voice capture and transcript routing on Quest hardware.
- Conversational latency masking using immediate acoustic fillers ("Hmm...", "Let's see...").
- Streaming LLM inference with concurrent sentence-by-sentence text-to-speech synthesis.
- Procedural World-Space spatial flashcards in 3D with flip animations and debounced Touch controller inputs.
- Dual-provider AI support: hybrid cloud generation via Google Gemini and experimental offline edge inference using quantized GGUF models.

---

## 2. Why Socratic?

Passive reading and rote memorization are notoriously inefficient for deep conceptual retention. When students review technical subjects—like calculus, physics, or systems engineering—they often look at a solution, feel an illusion of competence, and move on without actively building mental models. Traditional flashcards test recall, but they cannot diagnose *why* a student answered incorrectly or guide them through their misunderstanding.

Integrating a large language model acting as a Socratic tutor directly inside Virtual Reality creates an active study environment. VR eliminates external visual distractions, while voice-driven Socratic inquiry forces active verbal synthesis. Instead of lecturing, the tutor asks concise, targeted questions that nudge the learner to identify gaps in their reasoning and deduce the answer independently.

---

## 3. Architecture at a Glance

```text
┌─────────────────┐       ┌─────────────────┐       ┌────────────────────────┐
│  Meta Quest 3   │       │ Unity 6 Engine  │       │     AI Layer           │
│                 │       │                 │       │                        │
│ Headset Mic     ├──────►│ IVoiceRecognizer├──────►│ SocraticTutorPipeline  │
│                 │       │                 │       │           │            │
│                 │       │                 │       │           ▼            │
│                 │       │                 │       │ ┌────────────────────┐ │
│ Touch Controller├──────►│XRInteraction   │       │ │ Local LLM / Cloud  │ │
│                 │       │Input            │       │ └─────────┬──────────┘ │
│                 │       │                 │       │           │ Tokens     │
│                 │       │                 │       │           ▼            │
│ Spatial 3D UI   │◄──────┤ SpatialCard3D   │◄──────┤ SentenceAccumulator    │
│                 │       │                 │       │           │ Sentences  │
│                 │       │                 │       │           ▼            │
│ Headset Audio   │◄──────┤ AndroidNativeTTS│◄──────┤ Android Native TTS     │
└─────────────────┘       └─────────────────┘       └────────────────────────┘
```

---

## 4. Tech Stack

- **Game Engine:** Unity 6 (6000.0 LTS)
- **Target Platform:** Meta Quest 3 / Horizon OS (Android 12L / API Level 32)
- **XR SDKs:** Meta XR Core SDK, Meta XR Interaction SDK, Oculus Touch Input Module
- **Cloud LLM Client:** Google Gemini 1.5 Flash via direct `UnityWebRequest` (AOT/IL2CPP compatible)
- **Edge LLM Inference:** `ai.undream.llm` (llama.cpp wrapper) with Qwen quantized GGUF weights
- **Speech Synthesis (TTS):** Native Android AOSP `TextToSpeech` via custom JNI proxy
- **Typography & Math:** TextMeshPro with runtime LaTeX formula segmentation

---

## 5. Interesting Technical Problems Solved

- **Masking Time-to-First-Token (TTFT) Latency:** Initializing mobile inference and processing prompt context in VR causes a 2–3 second delay. I resolved this by firing instant natural acoustic fillers ("Hmm...", "Let's see...") immediately upon speech cut-off, transforming an awkward freeze into a natural conversational pause.
- **Concurrent Sentence-Level Audio Streaming:** Streaming individual tokens to TTS sounds broken, while waiting for the entire response adds unacceptable lag. I built `SentenceAccumulator` to partition token deltas at sentence terminators (`.`, `?`, `!`), enabling the voice synthesizer to play the first sentence while later sentences are still being generated.
- **Hardware Thread Budgeting for 72 FPS in VR:** Running unconstrained on-device LLM inference across all CPU cores drops framerate to ~25 FPS, inducing motion sickness. I restricted the inference worker pool to 4 dedicated cores, reserving remaining cores for the Unity render and tracking pipeline to guarantee a smooth 72 FPS.
- **AOT/IL2CPP-Safe HTTP & Rate Limit Handling:** Standard enterprise .NET HTTP client SDKs frequently fail under IL2CPP ahead-of-time compilation. I authored a clean, zero-dependency REST client on `UnityWebRequest` featuring exponential backoff retries for HTTP 429 rate limits and single-turn JSON repair prompts.
- **Spatial UI Flip Without Backface Artifacts:** Standard World-Space Canvases suffer from text-mirroring and backface culling issues when rotated 180°. I engineered an animated scale-inversion flip ($X: 1 \to 0 \to 1$) that swaps card faces cleanly without shader complexity or inverted raycast colliders.

---

## 6. Repository Structure

```text
socratic-tutor-vr-clean/
├── .gitignore                   # Comprehensive Unity and .NET build ignore rules
├── LICENSE                      # MIT Open Source License
├── README.md                    # Project overview, architecture, and technical breakdown
├── CHANGELOG.md                 # Semantic version log of prototype releases
├── docs/                        # In-depth engineering specifications
│   ├── ARCHITECTURE.md          # Sequence diagrams, state machines, and class hierarchies
│   ├── DECISIONS.md             # Architecture Decision Records (ADRs) with rationale
│   ├── AI_INTEGRATION.md        # Prompt design, response schemas, and cost breakdowns
│   └── AUDIT_REPORT.md          # Internal security audit and sanitization report
└── Assets/
    └── Scripts/
        ├── AI/                  # Tutor pipeline, Gemini provider, and local inference engine
        ├── Core/                # Sentence accumulator, functional Result types, and data models
        ├── UI/                  # Procedural 3D World-Space cards and flip animations
        └── VR/                  # Quest controller raycasts, mic contracts, and Android TTS JNI
```

---

## 7. What I'd Do Next

- **Persistent Session & Error Tracking:** Persist historical Socratic dialog sessions and card difficulty ratings into local SQLite on the Quest.
- **Voice Activity Detection (VAD) Fine-Tuning:** Replace static audio cut-offs with an on-device Silero VAD model to distinguish between thought pauses and user turn completion.
- **Expressive Neural TTS Voices:** Transition from Android's default robotic TTS to an on-device neural voice model (such as Piper or Sherpa-ONNX) for warm, natural prosody.
- **Spatial Anchor Pinning:** Allow the learner to pin study cards persistently in their real physical room using Meta Quest Spatial Anchors and Scene API.

---

## 8. Author / Contact

- **Author:** Juan Pedro Cogliano
- **GitHub:** [github.com/JuanPedro7](https://github.com/JuanPedro7)
- **LinkedIn:** [linkedin.com/in/juanpcogliano](https://linkedin.com/in/juanpcogliano)
