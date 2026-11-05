# File Inventory

Files that are typically inspected and/or modified when updating OpenTelemetry gen-ai semantic conventions.

## Constants

| File | Purpose |
|------|---------|
| `src/Libraries/Microsoft.Extensions.AI/OpenTelemetryConsts.cs` | All OTel attribute and metric name constants. New attributes/metrics always get constants added here first. |

## Instrumentation Clients

These files contain the actual telemetry emission logic. Each wraps a different AI capability with OTel spans, metrics, and logs.

| File | Capability | Key Sections |
|------|-----------|--------------|
| `src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClient.cs` | Chat completion | Activity creation, attribute emission, message serialization, streaming support, metrics |
| `src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryImageGenerator.cs` | Image generation | Activity creation, attribute emission |
| `src/Libraries/Microsoft.Extensions.AI/Embeddings/OpenTelemetryEmbeddingGenerator.cs` | Embeddings | Activity creation, attribute emission |
| `src/Libraries/Microsoft.Extensions.AI/SpeechToText/OpenTelemetrySpeechToTextClient.cs` | Speech-to-text | Activity creation, attribute emission |
| `src/Libraries/Microsoft.Extensions.AI/TextToSpeech/OpenTelemetryTextToSpeechClient.cs` | Text-to-speech | Activity creation, attribute emission |
| `src/Libraries/Microsoft.Extensions.AI/Realtime/OpenTelemetryRealtimeClientSession.cs` | Realtime sessions | Activity creation, attribute emission |
| `src/Libraries/Microsoft.Extensions.AI/Realtime/OpenTelemetryRealtimeClient.cs` | Realtime client wrapper | Delegates to session |
| `src/Libraries/Microsoft.Extensions.AI/Files/OpenTelemetryHostedFileClient.cs` | Hosted file management | Activity creation, attribute emission |

## Function Invocation / Tool Orchestration

These files handle `execute_tool`, `invoke_agent`, and `invoke_workflow` spans:

| File | Purpose |
|------|---------|
| `src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs` | Chat-based tool orchestration |
| `src/Libraries/Microsoft.Extensions.AI/Realtime/FunctionInvokingRealtimeClientSession.cs` | Realtime tool orchestration |
| `src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationProcessor.cs` | Shared function invocation logic (used by both chat and realtime) |
| `src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationHelpers.cs` | Shared function invocation helpers |
| `src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationLogger.cs` | Shared function invocation logging |

## Shared Code

| File | Purpose |
|------|---------|
| `src/Libraries/Microsoft.Extensions.AI/Common/OpenTelemetryLog.cs` | Shared `[LoggerMessage]` definitions for OTel events (e.g. exception recording) |
| `src/Libraries/Microsoft.Extensions.AI/TelemetryHelpers.cs` | Shared telemetry helper methods (at library root, not Common/) |

## Tests

| File | Tests For |
|------|----------|
| `test/Libraries/Microsoft.Extensions.AI.Tests/ChatCompletion/OpenTelemetryChatClientTests.cs` | Chat client telemetry |
| `test/Libraries/Microsoft.Extensions.AI.Tests/Image/OpenTelemetryImageGeneratorTests.cs` | Image generator telemetry |
| `test/Libraries/Microsoft.Extensions.AI.Tests/Embeddings/OpenTelemetryEmbeddingGeneratorTests.cs` | Embedding generator telemetry |
| `test/Libraries/Microsoft.Extensions.AI.Tests/SpeechToText/OpenTelemetrySpeechToTextClientTests.cs` | Speech-to-text telemetry |
| `test/Libraries/Microsoft.Extensions.AI.Tests/TextToSpeech/OpenTelemetryTextToSpeechClientTests.cs` | Text-to-speech telemetry |
| `test/Libraries/Microsoft.Extensions.AI.Tests/Realtime/OpenTelemetryRealtimeClientTests.cs` | Realtime session telemetry |
| `test/Libraries/Microsoft.Extensions.AI.Tests/Files/OpenTelemetryHostedFileClientTests.cs` | Hosted file client telemetry |

To discover any additional test files: `dir test\Libraries\Microsoft.Extensions.AI.Tests\ -Recurse -Filter "OpenTelemetry*Tests.cs"`

## Version References

The semantic conventions version is referenced in a doc comment in specific OpenTelemetry* instrumentation client files. When bumping the version, update all files that match the grep below — not all OpenTelemetry* files contain the version reference.

### Current wording (pre-migration)

The reference today still reads (carried over from when conventions lived in the core `open-telemetry/semantic-conventions` repo):

```csharp
/// This class provides an implementation of the Semantic Conventions for Generative AI systems v1.XX,
/// defined at <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/" />.
```

### Target wording (post-migration)

After the GenAI conventions moved to [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai), the doc comment should call out the standalone repo and use a GenAI-namespaced version (`vX.Y.Z`). Until `semantic-conventions-genai` publishes a release or schema URL, take that version from the `changelog.d/` fragment snapshot you audited — not from `versions.env` (whose `SEMCONV_VERSION` is the core semconv dependency, not the GenAI version):

```csharp
/// This class provides an implementation of the GenAI Semantic Conventions vX.Y.Z,
/// defined at <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/" />.
```

The `<see href>` URL is kept for now: the published page at `https://opentelemetry.io/docs/specs/semconv/gen-ai/` currently resolves only to a "Moved" stub that points at `semantic-conventions-genai` and no longer renders the spec. Leave the URL in place until OpenTelemetry publishes a canonical URL for the conventions (the `semantic-conventions-genai` `README.md` `## Schema URL` section is `TODO`), then retarget it. Do not use that page as the spec source — read `docs/gen-ai/` and `model/<area>/` in `semantic-conventions-genai` instead.

### Wording migration

This wording change is a **one-time edit** that should ride along with the
**next convention-update PR** (not a separate cosmetic PR). Until that
update lands, both wordings may coexist transiently in the codebase.

### Finding occurrences during the transition

Use a regex that matches both wordings:

```bash
grep -rEn "Semantic Conventions for Generative AI systems v|GenAI Semantic Conventions v" src/Libraries/Microsoft.Extensions.AI/
```

Once every file has been migrated to the target wording, the regex can be
simplified back to a single literal:

```bash
grep -rn "GenAI Semantic Conventions v" src/Libraries/Microsoft.Extensions.AI/
```

## Provider-specific instrumentation

The new conventions repo also covers provider-specific areas (`openai`,
`anthropic`, `aws-bedrock`, `azure-ai-inference`). Provider-specific
attributes like `openai.*` belong in the **provider package**, not in
`Microsoft.Extensions.AI`:

| Upstream area | dotnet/extensions location |
|---|---|
| `openai` | `src/Libraries/Microsoft.Extensions.AI.OpenAI/` (e.g. `OpenAIClientExtensions.cs` for `openai.api.type` mapping) |
| `anthropic` | Out of scope for `dotnet/extensions` today — implications land in [`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp). See [SKILL.md §Cross-repo applicability](../SKILL.md#cross-repo-applicability). |
| `aws-bedrock` | Out of scope for `dotnet/extensions` today — implications land in the `BedrockRuntime` service library of [`aws/aws-sdk-net`](https://github.com/aws/aws-sdk-net). See [SKILL.md §Cross-repo applicability](../SKILL.md#cross-repo-applicability). |
| `azure-ai-inference` | Corresponding provider package, if/when one exists in this repo; otherwise out of scope |
| `mcp` | No instrumentation today — flag as a watch-list item if MCP changes appear |

Tests for provider-specific attributes live alongside the provider package
(e.g. `test/Libraries/Microsoft.Extensions.AI.OpenAI.Tests/`).
