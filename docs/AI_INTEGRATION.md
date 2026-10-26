# AI Integration Architecture

This document describes how the AI tutoring engine connects to both cloud LLMs (Google Gemini) and edge on-device inference (GGUF via LlamaLib/llama.cpp) on Meta Quest 3.

---

## 1. System Prompt Design

The Socratic tutor operates under strict pedagogical and acoustic constraints. Because speech synthesis latency scales directly with token count, responses are constrained to guided inquiries rather than didactic explanations.

```text
You are a Socratic tutor. Be concise: maximum 2 sentences per response.
Never provide direct answers; ask targeted questions that guide the student to deduce the solution.
End each sentence with clear terminal punctuation (. ? !), as your response is synthesized to speech sentence-by-sentence in real time.
```

### Engineering Rationale
1. **Never Give the Direct Answer:** Enforces the Socratic method while naturally shortening the response (asking a guiding question takes far fewer tokens than explaining a concept).
2. **Maximum 2 Sentences:** Caps token generation to under 80 tokens, keeping total inference duration under 3 seconds on mobile XR chipsets.
3. **Explicit Terminal Punctuation:** Ensures that `SentenceAccumulator` reliably encounters sentence boundaries for the TTS engine.

---

## 2. Expected Response Formats

### A. Conversational Tutor Stream
- **Output:** Raw token stream emitting plain conversational text.
- **Delivery:** Incremental tokens delivered to `Action<string> onChunk`.
- **Parsing:** Filtered by `SentenceAccumulator` into discrete sentences.

### B. Flashcard Generation (Cloud Extraction)
- **Output:** JSON array with strict escaping rules for mathematical expressions.
- **Schema:**
  ```json
  [
    {
      "front": "What is the physical meaning of the divergence of a vector field?",
      "back": "It represents the net outward flux per unit volume from an infinitesimal region around a point."
    }
  ]
  ```
- **LaTeX Math Handling:** Enforces standard LaTeX enclosed in `$ ... $` for inline math and `$$ ... $$` for display blocks. All backslashes are escaped (`\\frac`, `\\Delta`) to maintain JSON compliance.

---

## 3. Resiliency, Rate Limits & Failure Handling

| Failure Mode | Detection | Mitigation Strategy |
| :--- | :--- | :--- |
| **HTTP 429 (Rate Limit)** | Status code 429 in `UnityWebRequest` | Exponential backoff retry with base delay ($2^n$ seconds, up to 3 retries). |
| **Malformed JSON** | `JsonException` during deserialization | Single targeted repair turn prompting the model to re-emit strictly the JSON array. |
| **Markdown Code Blocks** | Response wrapped in ````json ... ```` | `GeminiAiProvider.StripMarkdownCodeBlocks` strips fences before parsing. |
| **Model Load Timeout** | Exceeding 180s loading `.gguf` | `LocalLlmEngine` times out and releases concurrency semaphore safely. |
| **TTS Initialization Failure** | Missing voice package on Horizon OS | Pipeline gracefully degrades to visual typewriter text on the spatial card. |

---

## 4. Cost & Latency Profile per 15-Minute Session

| Metric | Cloud Endpoint (Gemini 1.5 Flash) | Edge On-Device (Qwen 1.5B GGUF) |
| :--- | :--- | :--- |
| **Inference Cost** | ~$0.001 per study session (Free Tier eligible) | **USD $0.00 (100% Free & Offline)** |
| **Time-to-First-Token (TTFT)** | ~600ms – 1,100ms (Network dependent) | ~1,200ms (Post-warmup) |
| **Token Generation Rate** | ~40 tokens/sec | ~3.0 tokens/sec (4-core Snapdragon XR2 Gen 2) |
| **Acoustic Masking** | Conversational filler ("Hmm...") | Conversational filler ("Let's see...") |
| **Privacy Profile** | Encrypted transit to cloud API | Zero data leaves headset; 100% on-device |

---

## 5. Security & Key Management (BYOK)

The project adheres strictly to **Bring-Your-Own-Key (BYOK)** principles:
- No hardcoded keys exist in source code, prefabs, or ScriptableObjects.
- API keys are injected at runtime via constructor or encrypted local storage (`PlayerPrefs` with Android KeyStore cipher).
- URLs containing query parameters are never logged to `Debug.Log` or remote telemetry.
