# System Architecture

## 1. High-Level System Flow

The diagram below illustrates how user speech captured in Meta Quest 3 flows through transcription, conversational latency masking, streaming LLM inference, sentence-boundary chunking, and spatial presentation.

```mermaid
sequenceDiagram
    autonumber
    actor User as Learner (Quest 3)
    participant VR as SpatialCard3D / Input
    participant Pipe as SocraticTutorPipeline
    participant STT as IVoiceRecognizer
    participant TTS as AndroidNativeTTS
    participant LLM as LocalLlmEngine / Gemini
    participant Acc as SentenceAccumulator

    User->>VR: Trigger PTT / Voice Utterance
    VR->>Pipe: StartListening()
    Pipe->>STT: StartListening()
    User-->>STT: Speaks question or thought
    Pipe->>STT: StopAndTranscribeAsync()
    STT-->>Pipe: Transcribed Text
    Pipe->>TTS: Speak("Hmm...") [Immediate Acoustic Filler]
    Pipe->>LLM: CompleteAsync(Prompt, onChunk)
    
    loop Streaming Token Generation
        LLM-->>Pipe: onChunk(deltaToken)
        Pipe->>VR: Update Typewriter UI (Per Token)
        Pipe->>Acc: AppendFragment(deltaToken)
        opt Sentence Boundary Reached (. ? !)
            Acc-->>Pipe: Complete Sentence
            Pipe->>TTS: Speak(Sentence) [Concurrent Speech]
        end
    end

    Pipe->>Acc: FlushRemaining()
    Acc-->>Pipe: Trailing Sentence
    Pipe->>TTS: Speak(Trailing Sentence)
```

---

## 2. Tutor Conversational State Machine

```mermaid
stateDiagram-v2
    [*] --> Idle: App Launch / Scene Ready
    
    Idle --> Listening: User Trigger / Auto VAD
    Listening --> Listening: Capturing Microphone Stream
    Listening --> ProcessingAudio: Speech Silence Detected
    
    ProcessingAudio --> EmittingFiller: STT Transcribed
    EmittingFiller --> StreamingInference: Immediate Filler Spoken
    
    state StreamingInference {
        [*] --> BufferingTokens
        BufferingTokens --> AccumulatingSentence: Token Delta Received
        AccumulatingSentence --> SynthesizingSpeech: Punctuation Boundary Met (. ? !)
        SynthesizingSpeech --> BufferingTokens: Sentence Dispatched to TTS Queue
    }
    
    StreamingInference --> WaitingForUser: Generation Complete & Audio Flushed
    WaitingForUser --> Listening: Follow-up Question
    WaitingForUser --> Idle: Session Closed / Exit
```

---

## 3. Class Structure & Layer Decoupling

The project follows Clean Architecture principles, ensuring that core algorithms and domains remain completely independent of Unity or hardware SDKs.

```mermaid
classDiagram
    class SocraticTutorPipeline {
        +EnsureReadyAsync()
        +StartListening()
        +StopVoice()
        +ConverseAsync()
    }

    class IAiProvider {
        <<interface>>
        +RecommendedChunkSize: int
        +GenerateCandidatesAsync(textChunk): Result~CardCandidate~
    }

    class GeminiAiProvider {
        -string _apiKey
        -string _modelName
        +GenerateCandidatesAsync()
    }

    class LocalLlmEngine {
        +CompleteAsync(prompt, onChunk): Result~string~
        +PrewarmAsync(prompt): Result~bool~
    }

    class IVoiceRecognizer {
        <<interface>>
        +EnsureMicrophonePermissionAsync()
        +StartListening()
        +StopAndTranscribeAsync()
    }

    class AndroidNativeTTS {
        +EnsureInitializedAsync()
        +Speak(text)
        +Stop()
    }

    class SentenceAccumulator {
        +AppendFragment(fragment): IReadOnlyList~string~
        +FlushRemaining(): string
    }

    class SpatialCard3D {
        +Initialize(eyeAnchor)
        +DisplayCard(question, answer)
        +RevealAnswer()
    }

    class XRInteractionInput {
        +GetAimRay(): Ray
        +IsConfirmPressed(): bool
        +TryGetWristPose(pos, rot): bool
    }

    SocraticTutorPipeline --> IVoiceRecognizer : uses
    SocraticTutorPipeline --> AndroidNativeTTS : uses
    SocraticTutorPipeline --> SentenceAccumulator : partitions with
    SocraticTutorPipeline --> LocalLlmEngine : invokes
    GeminiAiProvider ..|> IAiProvider : implements
    SpatialCard3D --> XRInteractionInput : raycast target
```
