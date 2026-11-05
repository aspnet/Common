# CCA Prompt Template

Template for generating a structured prompt suitable for delegating convention update work to Copilot Coding Agent (CCA) on github.com.

## Template

Fill in the bracketed sections based on the analysis of the upstream input — a release, PR set, `changelog.d/` snapshot, or date range from [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai) (or, for catch-up work, from the consolidated `open-telemetry/semantic-conventions` repo).

---

```markdown
## Background

The OpenTelemetry GenAI semantic conventions changes summarized below
require compensating updates in dotnet/extensions.

Source of changes: {ONE_OF: release URL | changelog.d/ ref URL | PR set | date range}
{IF_RELEASE} Release notes: {RELEASE_URL}
{IF_CHANGELOG_SNAPSHOT} changelog.d/ snapshot: {CHANGELOG_REF_URL} (Towncrier news fragments at the time of audit)
{IF_DATE_RANGE} Date range: PRs merged to `open-telemetry/semantic-conventions-genai` `main` between {START} and {END}

> The GenAI conventions are maintained in [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai), which also covers `mcp`, `openai`, `anthropic`, `aws-bedrock`, and `azure-ai-inference` areas (they were previously managed in the consolidated `semantic-conventions` repo); see the skill's Migration Note for context. Compensating changes for `anthropic` land in [`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp); for `aws-bedrock` they land in the `BedrockRuntime` service library of [`aws/aws-sdk-net`](https://github.com/aws/aws-sdk-net) — flag both as out-of-scope here and open follow-ups there. See the skill's [Cross-repo applicability](https://github.com/dotnet/extensions/blob/main/.github/skills/update-otel-genai-conventions/SKILL.md#cross-repo-applicability) section.

Key upstream PRs:
{FOR_EACH_UPSTREAM_PR}
- [{PR_TITLE}]({PR_URL}) — area: `{AREA}`
{END_FOR_EACH}

## Changes Audit

| Area | Semantic Convention Change | Upstream PR | Classification | Action Required |
|---|---|---|---|---|
{FOR_EACH_CHANGE}
| `{AREA}` | `{ATTRIBUTE_OR_CHANGE_NAME}` | [open-telemetry/semantic-conventions-genai#{PR_NUMBER}]({PR_URL}) | {CLASSIFICATION} | {ACTION} |
{END_FOR_EACH}

## Required Changes

### 1. Version References

Update the GenAI semantic conventions doc-comment reference to `v{NEW_VERSION}` in ALL OpenTelemetry* client files that carry a version comment. If this is the PR that migrates the doc-comment wording from the pre-migration form, also reword each occurrence:

{LIST_ALL_FILES_WITH_VERSION_REFERENCE}

Pre-migration wording:
```csharp
/// Semantic Conventions for Generative AI systems v{OLD_VERSION},
```
→ target wording:
```csharp
/// GenAI Semantic Conventions v{NEW_VERSION},
```

If the wording was already migrated in an earlier PR, just bump the version:
```csharp
/// GenAI Semantic Conventions v{OLD_VERSION},
```
→
```csharp
/// GenAI Semantic Conventions v{NEW_VERSION},
```

### 2. New Constants

Add these constants to `src/Libraries/Microsoft.Extensions.AI/OpenTelemetryConsts.cs` (or to the corresponding provider package's constants file for `openai.*`, `anthropic.*`, etc.):

{FOR_EACH_NEW_CONSTANT}
In the `{PARENT_CLASS}` nested class (`{PARENT_CLASS}` corresponds to the upstream area: `GenAI` for `gen-ai/*`, `MCP` for `mcp/*`, `OpenAI` for `openai/*`, etc.):
```csharp
public const string {CONSTANT_NAME} = "{ATTRIBUTE_NAME}";
```
{END_FOR_EACH}

### 3. Attribute Emission

{FOR_EACH_NEW_ATTRIBUTE}
#### 3.{N}. `{ATTRIBUTE_NAME}` in `{CLIENT_FILE}`

{DESCRIPTION_OF_WHERE_AND_HOW_TO_EMIT}

Current code context (around line {LINE_NUMBER}):
```csharp
{EXISTING_CODE_SNIPPET}
```

Add:
```csharp
{NEW_CODE_TO_ADD}
```
{END_FOR_EACH}

### 4. Tests

Update tests in `{TEST_FILE_PATH}`:

{FOR_EACH_TEST_UPDATE}
- {EXISTING_OR_NEW}: {DESCRIPTION_OF_ASSERTION_TO_ADD}
{END_FOR_EACH}

Reference the `update-otel-genai-conventions` skill in `.github/skills/` for:
- Migration context in `SKILL.md` (§Migration Note)
- Implementation patterns in `references/implementation-patterns.md` (including area placement guidance)
- Testing guide in `references/testing-guide.md`
- Review checklist in `references/review-checklist.md`

## Validation

After implementing changes:
1. Restore, generate the AI-filtered solution, build, and run the tests using the Linux/macOS commands in `.github/skills/update-otel-genai-conventions/references/build-commands.md`
2. If the public API surface changed, run `pwsh ./scripts/MakeApiBaselines.ps1` and keep only the baselines for the libraries actually changed
3. Verify no remaining references to the old version using the transitional regex from `references/file-inventory.md`: `grep -rEn "Semantic Conventions for Generative AI systems v{OLD_VERSION}|GenAI Semantic Conventions v{OLD_VERSION}" src/Libraries/Microsoft.Extensions.AI/`
```

---

## Prompt Quality Guidelines

Based on analysis of successful CCA prompts (PRs #7379, #7382, #7322 — all predate the move, from when these conventions were managed in the consolidated `semantic-conventions` repo, but the prompt-shape lessons still apply):

### What makes a good prompt

1. **Exact file paths** — always include full relative paths from repo root
2. **Current code context** — show the existing code around the modification point with line numbers
3. **Expected code** — show what the new code should look like
4. **Constant values** — specify the exact string values for new OTel attribute names
5. **Test expectations** — specify which test file and whether to augment existing tests or create new ones
6. **Validation commands** — include the build/test commands to run
7. **Disambiguated PR references** — always use the `open-telemetry/semantic-conventions-genai#NNN` form (or `open-telemetry/semantic-conventions#NNN` for catch-up) so PR numbers don't collide across repos
8. **Area on every change row** — so the implementer knows whether each item belongs in `Microsoft.Extensions.AI` or a provider package

### What to avoid

1. **Vague instructions** — "update the tests" → specify exactly which assertions to add
2. **Missing files** — forgetting to update version references in all OpenTelemetry* files
3. **Wrong approach** — specifying `Activity.AddEvent` when `ILogger` should be used for events
4. **Incomplete scope** — only covering chat client when embedding generator also needs changes
5. **Wrong package** — putting `openai.*` (or any provider-specific) attributes in `Microsoft.Extensions.AI` instead of the provider package
6. **Bare `#NNN` PR refs** — PR numbers are not interchangeable between the two repos

### Prompt size guidance

- **Simple version bump** (few code changes): ~1,000–2,000 characters
- **New attributes/metrics** (moderate changes): ~3,000–5,000 characters
- **Behavioral changes** (complex): ~5,000–8,000 characters
- **Audit table only** (version bump with analysis): use the concise audit table format from PR #7322
