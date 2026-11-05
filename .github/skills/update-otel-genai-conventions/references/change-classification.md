# Change Classification

Taxonomy for classifying GenAI semantic-conventions changes from PRs, CHANGELOG snapshots, date ranges, or releases (when they exist) in [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai). `area:gen-ai` PRs from the consolidated `semantic-conventions` repo (where these conventions were previously managed) classify the same way. Use this to assess each change's impact on dotnet/extensions.

## Areas

The `semantic-conventions-genai` repo hosts conventions for several areas. Identify the **area** of each change from its path: `gen-ai`, `mcp`, `openai`, and `aws-bedrock` have a YAML registry under `model/<area>/`; `anthropic` and `azure-ai-inference` have no `model/` registry today and exist only as provider doc pages under `docs/gen-ai/<provider>.md`. All human-readable docs live under `docs/gen-ai/`, not `docs/<area>/`:

| Area | dotnet/extensions location |
|---|---|
| `gen-ai`, `gen-ai/agent` | `Microsoft.Extensions.AI` core (e.g. `OpenTelemetryChatClient`) |
| `mcp` | No instrumentation today — classify all `mcp` changes as 🟢 *No client exists* unless and until an MCP client is added |
| `openai` | `Microsoft.Extensions.AI.OpenAI` (provider package) |
| `anthropic` | Out of scope for `dotnet/extensions` (no provider package). Classify as 🟢 *No client exists* **from the perspective of this repo**. The compensating change actually lands in [`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp) — see [SKILL.md §Cross-repo applicability](../SKILL.md#cross-repo-applicability). |
| `aws-bedrock` | Out of scope for `dotnet/extensions` (no provider package). Classify as 🟢 *No client exists* **from the perspective of this repo**. The compensating change actually lands in the `BedrockRuntime` service library of [`aws/aws-sdk-net`](https://github.com/aws/aws-sdk-net) — see [SKILL.md §Cross-repo applicability](../SKILL.md#cross-repo-applicability). |
| `azure-ai-inference` | Corresponding provider package if/when one exists in this repo; otherwise classify as 🟢 *No client exists* |

When the area is provider-specific (`openai`, `anthropic`, `aws-bedrock`, `azure-ai-inference`), the compensating change usually belongs in the corresponding provider package, **not** in `Microsoft.Extensions.AI`. For `anthropic` ([`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp)) and `aws-bedrock` (the `BedrockRuntime` service library of [`aws/aws-sdk-net`](https://github.com/aws/aws-sdk-net)) this provider package lives in another SDK repo entirely; when running this skill in `dotnet/extensions`, audit-flag the change but classify it as out-of-scope and link the upstream change so a follow-up can be opened in the right SDK repo. When running this skill **in** that SDK repo, treat the area as in-scope and route the change to that repo's own constants/instrumentation files.

## Classification Categories

### 🟢 No Action Required

| Type | Description | Example |
|------|-------------|---------|
| **N/A — No client exists** | Change affects a capability we don't implement (e.g. `retrieval`, `memory`) | `gen_ai.retrieval.*` attributes |
| **Already implemented** | Change was already implemented in a prior PR | A change that was part of an earlier draft spec we adopted |
| **Server-side only** | Change affects server/provider-side instrumentation, not client-side | Server span attributes |
| **Documentation only** | Clarification of existing semantics with no behavioral change | Rewording of attribute descriptions |
| **Constant not yet emitted** | New attribute defined upstream, but no OpenTelemetry* client in this repo populates a value for it | `gen_ai.usage.cache_creation.input_tokens` — defer the constant until a future PR adds an emission site |

> **No orphan constants.** A new constant in `OpenTelemetryConsts.cs` must only be added in a PR that also adds at least one emission site for it. If no client populates the attribute, classify the change as 🟢 *Constant not yet emitted* and defer adding the constant entirely — do not add it speculatively.

### 🟡 Minor Action Required

| Type | Description | Action |
|------|-------------|--------|
| **Version bump** | Convention version number changed | Update `v1.XX` in doc comments across all OpenTelemetry* files |
| **Stability promotion** | Attribute moved from experimental to stable | Usually no code change; note in audit table |

### 🔴 Code Change Required

| Type | Description | Action |
|------|-------------|--------|
| **New required attribute** | New attribute that should be emitted | Add constant, add emission code, add test assertion |
| **New metric** | New metric instrument defined | Add metric definition, emission, test |
| **Attribute rename** | Existing attribute renamed | Update constant value, verify backward compatibility |
| **New event** | New log/span event defined | Add event via `ILogger` + `[LoggerMessage]`, add test |
| **Behavioral change** | Change in how existing attributes are populated | Modify emission logic, update test expectations |
| **New operation name** | New `gen_ai.operation.name` value | Add detection logic, tests |
| **Schema change** | Change to JSON schema for serialized content (e.g. tool definitions) | Update serialization classes, `[JsonSerializable]` registration |

## Indicator Mapping

Use these indicators consistently in audit reports, implementation summaries, and PR descriptions:

| Indicator | Category | Meaning |
|---|---|---|
| 🟢 | No action required | No compensating code change is needed; explain why. |
| 🟡 | Minor action required | Small metadata, stability, or version-reference update. |
| 🔴 | Code change required | Runtime behavior, emission logic, metrics, events, serialization, or tests must change. |

## Impact Assessment Heuristic

For each gen-ai change in a release:

1. **Does it affect a capability we instrument?** Check the [file inventory](file-inventory.md) for matching client types.
   - No → classify as "N/A — No client exists"
2. **Is it already implemented?** Search `OpenTelemetryConsts.cs` for the attribute name.
   - Yes → classify as "Already implemented"
3. **Is it client-side or server-side?** Check the semantic convention's `span_kind` or context.
   - Server-side only → classify as "Server-side only"
4. **What kind of change is it?** Match to the categories above.
5. **How many files need modification?** Count affected files from the file inventory.
   - 1–2 files → Low complexity
   - 3–5 files → Medium complexity
   - 6+ files → High complexity (likely involves shared code or cross-cutting concern)

## Audit Table Format

When presenting the analysis, use this table format. The **Area** column lets reviewers see at a glance which dotnet/extensions package each change targets:

```markdown
| Area | Semantic Convention Change | Upstream PR | Classification | Action Required | Complexity |
|---|---|---|---|---|---|
| `gen-ai` | `gen_ai.new.attribute` | [open-telemetry/semantic-conventions-genai#1234](link) | New required attribute | Add constant + emission + test in `Microsoft.Extensions.AI` | Low |
| `gen-ai` | `gen_ai.deferred.attribute` | [open-telemetry/semantic-conventions-genai#2345](link) | Constant not yet emitted | Defer — no client populates this attribute in this PR | — |
| `mcp` | `mcp.tool.approval` | [open-telemetry/semantic-conventions-genai#3456](link) | N/A — No client | None — no MCP instrumentation today | — |
| `openai` | `openai.new.attribute` | [open-telemetry/semantic-conventions-genai#4567](link) | New required attribute | Add to `Microsoft.Extensions.AI.OpenAI` provider package | Low |
| `anthropic` | `anthropic.new.attribute` | [open-telemetry/semantic-conventions-genai#5678](link) | N/A — No client (in this repo) | Out of scope here — [`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp) is the actual target. Open a follow-up there. | — |
| `gen-ai` | Version reference | — | Version bump | Update doc comments to new wording | Low |
```

## PR Description Table Format

When preparing a PR description, adapt the audit table into a concise reviewer-facing table grouped or sorted by GenAI version (the version from the schema URL or CHANGELOG release header — independent of core semconv). Include every analyzed change, not just changes that required code edits.

```markdown
| Version | Area | Indicator | Semantic-conventions change | Classification | Compensating change / rationale |
|---|---|:---:|---|---|---|
| vX.Y.Z | `gen-ai` | 🔴 | `gen_ai.new.attribute` added | New required attribute | Added constant, emission, and tests in `{files}`. |
| vX.Y.Z | `gen-ai` | 🟡 | Version reference update | Version bump | Updated OpenTelemetry* doc comments to the new wording. |
| vX.Y.Z | `gen-ai` | 🟢 | Provider server span clarified | Server-side only | No client-side instrumentation change needed. |
| vX.Y.Z | `gen-ai` | 🟢 | `gen_ai.deferred.attribute` added upstream | Constant not yet emitted | No client populates this attribute today; constant will be added in the PR that adds emission. |
| vX.Y.Z | `openai` | 🔴 | `openai.new.attribute` added | New required attribute | Added to `Microsoft.Extensions.AI.OpenAI` provider package. |
| vX.Y.Z | `mcp` | 🟢 | `mcp.tool.approval` added | N/A — No client | No MCP instrumentation in this repo today. |
```

The final column should either describe the compensating change made or explain why no code change was made, such as "already implemented", "no local source exists", "no client exists", "server-side only", "documentation-only clarification", "no client populates this attribute today; constant deferred until a PR adds emission", "provider-specific area not instrumented in this repo", or "out of scope here — implications land in `anthropics/anthropic-sdk-csharp` (anthropic) or the `BedrockRuntime` library of `aws/aws-sdk-net` (aws-bedrock); follow-up opened in that repo".
