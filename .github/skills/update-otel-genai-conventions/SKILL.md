---
name: update-otel-genai-conventions
description: >-
  Analyze OpenTelemetry GenAI semantic-conventions changes (PRs, CHANGELOG
  snapshots, date ranges, or releases when they exist) and produce
  compensating change plans for dotnet/extensions. The conventions now live
  in their own repo at open-telemetry/semantic-conventions-genai and cover
  gen-ai, mcp, openai, anthropic, aws-bedrock, and azure-ai-inference areas;
  the previous home was open-telemetry/semantic-conventions under the
  area:gen-ai label. Use when asked to "update OTel conventions", "check
  semantic-conventions-genai", "plan gen-ai convention changes", "bump genai
  semconv version", review gen-ai/MCP/provider convention PRs, or when given
  a PR number/URL, CHANGELOG snapshot, date range, or release version from
  either repo. Also use for "update OpenTelemetry", "bump semconv version",
  or "what changed in semantic-conventions-genai".
agent: 'agent'
tools: ['github/*', 'sql']
---

# Update OTel GenAI Conventions

Analyze OpenTelemetry GenAI semantic-conventions changes — PRs, changelog snapshots, date ranges, or releases — primarily from [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai), and produce compensating updates in `dotnet/extensions`. See the [Migration Note](#migration-note) below for context, including where these conventions were previously managed.

## Migration Note

The OpenTelemetry GenAI semantic conventions are maintained in a dedicated
repo:
[`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai),
which also hosts `mcp`, `openai`, `anthropic`, `aws-bedrock`, and
`azure-ai-inference` conventions. They were previously managed in the
consolidated
[`open-telemetry/semantic-conventions`](https://github.com/open-telemetry/semantic-conventions)
repo under the `area:gen-ai` label.

Implications for this skill:

- **Primary input source** is `semantic-conventions-genai`. The
  consolidated `semantic-conventions` repo remains a **fallback** for
  catch-up audits, historical context, and any in-flight PR that started
  there previously.
- **Every PR is in scope for consideration**, because
  `semantic-conventions-genai` is specific to GenAI conventions. (In the
  consolidated `semantic-conventions` repo the `area:gen-ai` label scoped
  this work, and it still applies there for catch-up.) The repo does use
  more granular `area:*` labels (for example `area:mcp`, `area:inference`,
  `area:tools`, `area:embeddings`), which can help triage but are not
  required for scoping.
- **No releases yet** in `semantic-conventions-genai`.
  <!-- TODO: remove the "no releases yet" framing once semantic-conventions-genai ships its first release -->
  The repo manages its changelog with **Towncrier**: the `CHANGELOG.md`
  `Unreleased` section is intentionally empty (fragments are compiled into
  it only at release time), so the live "what's new" view is the set of
  news fragments under `changelog.d/`, each named `<upstream-PR>.<type>.md`
  (types include `enhancement`, `bugfix`, `breaking`, `clarification`). Pin
  a snapshot via commit SHA / ref for reproducible audits.
- **Release tagging is not proven yet.** Until the repo ships real GenAI
  releases, treat GitHub Releases plus the release tag's dereferenced commit
  as the workflow baseline, but actively compare it with other durable signals
  (CHANGELOG release headers, schema URLs, and observed tag naming). Report
  confidence in the approach used, and surface a PR-body caution if another
  release signal becomes clearly more dependable than the workflow baseline.
- **GenAI version is now independent** of core semconv: it tracks its own
  version line. The schema URL
  `https://opentelemetry.io/schemas/gen-ai/X.Y.Z` is intended to carry the
  gen-ai version, but is **not published yet** (the repo's `README.md`
  `## Schema URL` section is `TODO`). The repo's `versions.env` holds
  only the **core semconv dependency** (`SEMCONV_VERSION`, currently
  `v1.42.0`) and the Weaver toolchain version — it does **not** carry the
  GenAI convention version, so do not treat `SEMCONV_VERSION` as the GenAI
  version. Until a GenAI release or schema URL exists, there is no
  published GenAI version number; identify the update by its `changelog.d/`
  fragment snapshot, pinned to a commit SHA / ref / date.
- **Spec URL `https://opentelemetry.io/docs/specs/semconv/gen-ai/`
  currently resolves to a "Moved" stub** that states the GenAI
  conventions have moved to `semantic-conventions-genai` and is no longer
  maintained; it no longer renders the spec content. The `<see href>` in
  dotnet/extensions source files still points at this URL, so leave it for
  now but revisit retargeting it once OpenTelemetry publishes a canonical
  URL for the conventions (the repo's `README.md` `## Schema URL` section
  is currently `TODO`).
  <!-- TODO: once open-telemetry/semantic-conventions-genai publishes its
       canonical spec/schema URL, retarget the OpenTelemetry* doc-comment
       `<see href>` from the /docs/specs/semconv/gen-ai/ stub to that URL. -->
- **PR numbering** is not interchangeable across repos. Always
  disambiguate with `open-telemetry/semantic-conventions#NNN` or
  `open-telemetry/semantic-conventions-genai#NNN` when there is any risk
  of collision.
- **Doc-comment wording** in dotnet/extensions source still reads
  "Semantic Conventions for Generative AI systems v1.XX". The next
  convention-update PR should migrate this to "GenAI Semantic Conventions
  vX.Y.Z" — see [references/file-inventory.md §Version References](references/file-inventory.md#version-references).

When a referenced PR number doesn't resolve in `semantic-conventions-genai`,
check the consolidated `semantic-conventions` repo before assuming the
input is invalid.

## Cross-repo applicability

This skill lives in `dotnet/extensions` and its file paths, build
commands, and PR-description conventions are tuned for that repo. The
`semantic-conventions-genai` repo also hosts provider-specific areas
(`anthropic`, `aws-bedrock`) whose dotnet instrumentation lives in
**other** SDK repositories that we contribute to:

| Upstream area | Repository | Notes |
|---|---|---|
| `anthropic` | [`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp) | Anthropic's official .NET SDK. |
| `aws-bedrock` | [`aws/aws-sdk-net`](https://github.com/aws/aws-sdk-net) | AWS Bedrock instrumentation lives in the `BedrockRuntime` service library (`AWSSDK.BedrockRuntime`) inside the AWS SDK monorepo. |

The skill can optionally be applied in those repos with the following
adaptations:

- **Apply** the convention analysis, classification framework
  ([references/change-classification.md](references/change-classification.md)),
  audit-table shape, area routing, doc-comment wording target
  ("GenAI Semantic Conventions vX.Y.Z"), version-reference grep
  recipes, and PR-description shape
  ([references/pr-description.md](references/pr-description.md)).
- **Do not assume** dotnet/extensions-specific paths
  (`src/Libraries/Microsoft.Extensions.AI*/`), the
  `OpenTelemetryConsts.cs` constants layout, the API-baseline workflow,
  or the build/test commands in
  [references/build-commands.md](references/build-commands.md). Use the
  target repo's own conventions for code structure, constants
  organization, and validation.
- **Scope by repo**: when running in another repo, the in-scope upstream
  area is the one that repo instruments (e.g. `anthropic` in
  `anthropics/anthropic-sdk-csharp`, `aws-bedrock` in the
  `BedrockRuntime` library of `aws/aws-sdk-net`). Other areas are out
  of scope from that repo's perspective even though they remain in
  scope for `dotnet/extensions`.
- **Pre-flight** still applies — search open PRs in the target repo for
  prior coverage before producing a plan.

## Mode Detection

Determine the operating mode from the user's request:

| Signal | Mode |
|--------|------|
| User asks to "audit" current implementation or "check alignment" with conventions | **Mode 1: Audit** |
| User asks to "update for vX.Y" or "apply vX.Y changes" in autopilot / one-shot | **Mode 2: Autopilot** |
| User asks to "generate a prompt" or "delegate to Copilot" or "CCA prompt" | **Mode 3: CCA Prompt** |
| Running inside Copilot Coding Agent with a prompt referencing this skill | **Mode 4: CCA Implementation** |
| User is in `/plan` mode, asks to "plan" changes, or asks to "implement" / "apply" changes | **Mode 5: Plan-then-Implement** |
| User asks to `/review` or "review" convention changes | **Mode 6: Review** |

If unclear, default to **Mode 5** (Plan-then-Implement) and offer Mode 3 as an alternative.

## Input Handling

`semantic-conventions-genai` does not yet publish releases. Until it does,
the user typically provides one of:

- **PR references** in `semantic-conventions-genai` — full URL, `#NNN`, or
  `open-telemetry/semantic-conventions-genai#NNN` form. (No `area:` label
  filter is needed: the repo is gen-ai-focused by definition.)
- **A `changelog.d/` snapshot** — a commit SHA, branch ref, or a
  `https://github.com/open-telemetry/semantic-conventions-genai/tree/{ref}/changelog.d`
  URL pinning the Towncrier news fragments at a point in time. (The
  `CHANGELOG.md` `Unreleased` section stays empty until release, so use the
  fragments — not `CHANGELOG.md` — for unreleased work.)
- **A date range or "since last update"** — list of PRs merged to
  `semantic-conventions-genai`'s `main` between two refs / dates.
- **A release version or release URL** — once releases exist
  (`https://github.com/open-telemetry/semantic-conventions-genai/releases/tag/{version}`).

For **catch-up or historical work**, the consolidated
`semantic-conventions` repo is still valid input:

- A **semantic-conventions release version** (e.g. `v1.40.0`) → fetch from
  `https://github.com/open-telemetry/semantic-conventions/releases/tag/{version}`
  (filter to `area:gen-ai` PRs).
- A **release URL** from `semantic-conventions` → fetch the release notes directly.
- **PR references** from `semantic-conventions` — only the ones with `area:gen-ai`.
  Use the `open-telemetry/semantic-conventions#NNN` form to disambiguate
  from `semantic-conventions-genai` PR numbers.

When PR numbers are given without a full URL, default to
`semantic-conventions-genai` and fall back to the consolidated
`semantic-conventions` repo only if the PR doesn't exist in
`semantic-conventions-genai` or the user explicitly references it.

## Release Tagging Signal Analysis

For every audit or implementation pass that creates or refreshes an upstream-scan
tracking PR, include a concise release-tagging analysis. The current workflow baseline
is:

1. Read GitHub's latest release for `open-telemetry/semantic-conventions-genai`.
2. Resolve that release's `tag_name` through `refs/tags`, dereferencing annotated tags
   to the commit they point at.
3. Treat the release as ready for a maintained PR only when that resolved commit equals
   the PR body's recorded `upstream-scan-ref`.

Compare that baseline with all durable release signals available at the scanned ref:

- GitHub Releases and the resolved release tag commit.
- `CHANGELOG.md` release headers produced by Towncrier.
- Published schema URLs such as `opentelemetry.io/schemas/gen-ai/X.Y.Z`, once they
  exist.
- Stable tag naming patterns, if the repo begins publishing tags without GitHub
  Releases.

Report confidence using these meanings:

- `high`: the signal is published and tied to an exact commit or schema version that
  matches the audited GenAI conventions.
- `medium`: the signal identifies a version, but the exact commit relationship is not
  proven.
- `low`: the signal is inferred from non-release data such as fragments, core semconv
  dependency versions, or naming guesses.
- `none`: no published GenAI release signal exists.

If a different release signal becomes clearly dependable and conflicts with the
workflow baseline, keep the workflow behavior unchanged for that run, but add the
PR-description `> [!CAUTION]` block described in
[references/pr-description.md](references/pr-description.md). The block must name the
signal found and recommend the specific skill and/or workflow change needed.

### In-scope areas

The `semantic-conventions-genai` repo hosts conventions for several areas, all of which this skill
covers (with placement guidance in
[references/implementation-patterns.md](references/implementation-patterns.md)):

| Upstream area | Maps to in dotnet/extensions |
|---|---|
| `gen-ai`, `gen-ai/agent` | `Microsoft.Extensions.AI` core (e.g. `OpenTelemetryChatClient`) |
| `mcp` | Currently no instrumentation; forward-looking — flag as a watch-list item if changes appear |
| `openai` | `Microsoft.Extensions.AI.OpenAI` |
| `anthropic` | Out of scope for `dotnet/extensions` today (no provider package). Implications land in [`anthropics/anthropic-sdk-csharp`](https://github.com/anthropics/anthropic-sdk-csharp) — apply this skill there per [Cross-repo applicability](#cross-repo-applicability). |
| `aws-bedrock` | Out of scope for `dotnet/extensions` today (no provider package). Implications land in the `BedrockRuntime` service library of [`aws/aws-sdk-net`](https://github.com/aws/aws-sdk-net) — apply this skill there per [Cross-repo applicability](#cross-repo-applicability). |
| `azure-ai-inference` | Corresponding provider package, if/when one exists in this repo; otherwise out of scope |

When classifying a change, identify its area as follows. `gen-ai`,
`mcp`, `openai`, and `aws-bedrock` each have a YAML registry under
`model/<area>/`, so use that path. `anthropic` and `azure-ai-inference`
do **not** have a `model/` registry today; they are documented only as
provider pages under `docs/gen-ai/<provider>.md`. All human-readable
docs live under `docs/gen-ai/` (for example `docs/gen-ai/openai.md`,
`docs/gen-ai/mcp.md`, `docs/gen-ai/anthropic.md`), not under
`docs/<area>/`.

### Existing dotnet/extensions PR Preflight

For **Mode 1: Audit** and **Mode 5: Plan-then-Implement**, after resolving the requested input identifiers but before doing deeper analysis or creating a plan, search open pull requests in `dotnet/extensions` to determine whether another PR already appears to cover the requested update.

Search using the requested release version, CHANGELOG ref, date range, or upstream PR numbers, plus relevant terms such as `gen-ai`, `GenAI`, `semantic conventions`, `semantic-conventions-genai`, `semconv-genai`, `OpenTelemetry`, `OTel`, and any in-scope area name (`MCP`, `OpenAI`, `Anthropic`, `Bedrock`, `Azure AI Inference`) that matches the changes you're working from. If one or more likely matching PRs are open, report the PR number, title, author, URL, and the signal that matched. Then stop and state that the audit or plan is not proceeding because an open PR already appears to cover the update.

Do not silently ignore search failures. If GitHub search/listing is unavailable, report the problem and ask the user whether to proceed without the preflight.

A standing **upstream-scan tracking PR** (one carrying the `# meai-otel-genai-worker:state:begin` block) is the exception: it is the durable scan record, not a blocking duplicate. When the preflight surfaces it, continue rather than stopping -- the maintaining workflow owns how that PR is created and incremented.

### Analyzing the Release / PRs

1. **Fetch the release notes** or PR descriptions and identify all gen-ai changes
2. **Read** [references/file-inventory.md](references/file-inventory.md) to understand which files in this repo are affected
3. **Classify each change** using [references/change-classification.md](references/change-classification.md)
4. **Check current state** — read the current source files to determine what is already implemented vs. what needs new work
5. **Build a changes audit table** showing each semantic convention change, its classification, and required action

For Step 4, read the source files listed in [references/file-inventory.md](references/file-inventory.md) (`OpenTelemetryConsts.cs`, `OpenTelemetryChatClient.cs`, `OpenTelemetryEmbeddingGenerator.cs`, `Common/FunctionInvocationProcessor.cs`, and any other OpenTelemetry* files).

### PR Title and Description Guidance

When creating or updating a PR after implementing GenAI semantic-conventions changes (from either repo), follow [references/pr-description.md](references/pr-description.md) for the title format and the changes-table shape. For a recurring **upstream-scan tracking PR** (the kind carrying the `# meai-otel-genai-worker:state:begin` block), that reference also defines the full body template -- the implemented-changes table, the merged and in-flight applicability tables, and, at the very bottom, the machine-readable tracking state block (the body ends there).

---

## Mode 1: Audit

Audit the current gen-ai semantic conventions implementation against the latest published conventions to identify gaps, inconsistencies, or missed updates. Produces a plan that can be implemented locally (Mode 5) or delegated to CCA (Mode 3).

1. Complete the **Existing dotnet/extensions PR Preflight** above. If a matching open PR exists, report it and stop.
2. **Determine the current implemented version**: Read the version reference from `OpenTelemetryChatClient.cs` doc comment to identify which convention version the codebase claims to implement
3. **Check for version drift**: Verify every file with a gen-ai semantic conventions version reference uses the same version. Use the search command from [references/file-inventory.md](references/file-inventory.md#version-references). If files reference different versions, flag that as a critical gap requiring investigation.
4. **Fetch the latest convention spec**: Read the current conventions from the source of truth in [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai): `docs/gen-ai/` for human-readable docs (for example `docs/gen-ai/gen-ai-spans.md`, `docs/gen-ai/openai.md`) and `model/<area>/` for the YAML registry (`gen-ai`, `mcp`, `openai`, `aws-bedrock`). Note the published page at `https://opentelemetry.io/docs/specs/semconv/gen-ai/` is currently a "Moved" stub and no longer renders the spec. There is no `schema-snapshot/` directory, and the schema URL (`opentelemetry.io/schemas/gen-ai/X.Y.Z`) is not published yet (the repo `README.md` `## Schema URL` section is `TODO`). The repo's `versions.env` holds only the core semconv dependency (`SEMCONV_VERSION`) and the Weaver version — not a GenAI version — so do not treat `SEMCONV_VERSION` as the GenAI version. Until a GenAI release or schema URL exists, there is no published GenAI version number; identify the update by its `changelog.d/` fragment snapshot (commit SHA / ref / date). The GenAI convention version is independent of core semconv. Until releases exist in `semantic-conventions-genai`, use the latest `changelog.d/` news fragments or recently merged PRs as the "latest release notes" equivalent.
   <!-- TODO: once open-telemetry/semantic-conventions-genai publishes its schema URL (README ## Schema URL is currently TODO), switch the GenAI version source to that schema URL. -->
   Also complete the **Release Tagging Signal Analysis** above so any PR description states release-signal confidence and warns when a different dependable release signal appears.
5. **Read all current source files** listed in [references/file-inventory.md](references/file-inventory.md) to understand what is actually implemented
6. **Cross-reference**: For each attribute, metric, event, and operation name defined in the conventions:
   - Is the constant defined in `OpenTelemetryConsts.cs`?
   - Is it emitted in the relevant OpenTelemetry* client(s)?
   - Are version references consistent across all files?
   - Are tests covering the attribute/metric?
7. **Build an audit report** as a table:

   | Convention Item | Expected | Implemented | Gap |
   |----------------|----------|-------------|-----|
   | `gen_ai.request.attribute` | v1.XX | ✅ Yes / ❌ No / ⚠️ Partial | Description of gap |

8. **Produce a remediation plan** covering all identified gaps — formatted as either:
   - A **local plan** (Mode 5 format), or
   - A **CCA prompt** (Mode 3 format) suitable for delegation
   
   Ask the user which format they prefer, or produce both if requested.
9. **Verify this skill is still accurate** (same as Mode 6, step 6): compare skill content against the current codebase and call out any discrepancies

---

## Implementation Procedure

Modes 2, 4, and 5 share the same implementation flow. See [references/implementation-procedure.md](references/implementation-procedure.md).

---

## Mode 2: Autopilot

One-shot mode that analyzes the upstream input (release, PRs, CHANGELOG snapshot, or date range) and implements all changes in a single pass without intermediate review. Best for end-to-end execution when the user does not need a plan checkpoint.

1. Complete the **Input Handling** analysis above
2. Build an internal work plan in working memory (do not write `plan.md`):
   - Changes audit table with classification for each gen-ai change
   - Ordered list of implementation steps
3. Follow the **Implementation Procedure** above
4. Present a summary of all changes with the audit table showing what was implemented

---

## Mode 3: Generate CCA Prompt

Generate a structured prompt suitable for delegating to Copilot Coding Agent on github.com.

1. Complete the **Input Handling** analysis above
2. Read [references/prompt-template.md](references/prompt-template.md) for the template structure
3. Generate the prompt following the template, filling in:
   - Background with links to the upstream input (release URL, CHANGELOG snapshot ref, date range, or PR URLs)
   - Changes audit table (with **Area** column)
   - Required changes with exact file paths and code context from the current source
   - Test expectations referencing [references/testing-guide.md](references/testing-guide.md)
   - Validation steps
4. Present the prompt to the user for review

The generated prompt should reference this skill:
> Reference the `update-otel-genai-conventions` skill in `.github/skills/` for implementation patterns and testing guidance.

---

## Mode 4: CCA Implementation

When running inside Copilot Coding Agent (github.com) with a prompt that references this skill.

1. Parse the prompt to identify the required changes
2. Follow the **Implementation Procedure** above

---

## Mode 5: Plan-then-Implement

Generate a plan and (after user review/approval) implement it. Best when the user wants a checkpoint between analysis and execution. The runtime decides how to track work items (e.g., a task list, an in-memory queue, or a SQL `todos` table — whichever the agent already uses).

**Phase A: Plan** —

1. Resolve the user's input to one of: a release, PR identifiers, a `changelog.d/` fragment snapshot (commit SHA / ref), or a date range in `semantic-conventions-genai` (`open-telemetry/semantic-conventions-genai`). For catch-up work, accept upstream PRs from the consolidated `open-telemetry/semantic-conventions` repo with `area:gen-ai`
2. Complete the **Existing dotnet/extensions PR Preflight** above. If a matching open PR exists, report it and stop without creating a plan.
3. Complete the **Analyzing the Release / PRs** analysis above
4. Create `plan.md` with a problem statement linking to the upstream input (release URL, CHANGELOG snapshot ref, date range, or list of PR URLs — whichever applies), a changes audit table, and a numbered list of work items. Each work item should call out the file(s) to modify, what code/constants/attributes to add, and which tests to update.
5. Pause for user review/approval before proceeding to Phase B

**Phase B: Implement** —

6. Read the existing `plan.md`
7. Follow the **Implementation Procedure** above for each work item

---

## Mode 6: Review

Review changes to gen-ai conventions against past patterns and known gotchas.

1. Identify the changes to review (local diff or PR diff)
2. Read [references/review-checklist.md](references/review-checklist.md) for the full checklist
3. Read [references/historical-releases.md](references/historical-releases.md) for past PR patterns. This file is point-in-time reference data from skill creation and may not include recent releases.
4. Check each item against the checklist:
   - Sensitive data gating (`EnableSensitiveData`)
   - Fluent Activity API chain style
   - Code deduplication (shared `Common/` classes)
   - Test augmentation vs. new tests
   - Version reference completeness
   - Exception recording approach (ILogger vs Activity.AddEvent)
5. Report findings with references to past PRs where similar feedback was given
6. **Verify this skill is still accurate**: Compare SKILL.md and all reference files against the current codebase (the codebase may have evolved — files moved, patterns changed). Recommend updates only for durable, cross-release guidance: workflow steps, validation commands, repository conventions, stable implementation patterns, file paths, test infrastructure. Do **not** pollute skill files with release-specific findings (per-version audits, one-off attribute mappings, etc.) — capture those in the review report, PR description, or implementation summary instead. Update `historical-releases.md` only when explicitly asked.

---

## Gotchas

Critical knowledge from past PR reviews that should inform all modes:

- **Exception recording**: Use `ILogger` with `[LoggerMessage]`, NOT `Activity.AddEvent`. The OTel SDK handles `Exception` passed to `ILogger`. See `OpenTelemetryLog.cs` in `Common/`.
- **Sensitive data**: Attributes that could contain user data (e.g. `exception.message`, message content) must be gated behind `EnableSensitiveData`. When in doubt, gate it.
- **Fluent chains**: Use fluent Activity API chains (`.SetStatus(...).SetTag(...)`) rather than separate statements.
- **Shared code**: Cross-cutting concerns (like exception logging) shared across multiple OpenTelemetry* clients belong in `src/Libraries/Microsoft.Extensions.AI/Common/`. Before adding a new helper, method, or internal type, search `Common/`, `TelemetryHelpers.cs`, `OpenTelemetryLog.cs`, and sibling OpenTelemetry* clients for existing logic with the same purpose — reuse or extend instead of introducing a parallel implementation. When the same helper is needed in 2+ places, factor it into `Common/` from the start. The same applies to parallel internal types: if a sibling client already defines a type with the same shape (same properties, same role, e.g. `RealtimeOtelFunction` vs `OtelFunction`), unify them under a single shared definition rather than letting each client carry its own copy.
- **Test augmentation**: Prefer augmenting existing test assertions over creating new test methods. Check for existing tests that validate the same scenario.
- **Version references**: When bumping the convention version, update all files that match the transitional regex `grep -rEn "Semantic Conventions for Generative AI systems v|GenAI Semantic Conventions v" src/Libraries/Microsoft.Extensions.AI/` (handles both pre- and post-migration wording). The next convention update should also migrate the wording in lockstep — see [references/file-inventory.md §Version References](references/file-inventory.md#version-references). Not all OpenTelemetry* files contain this reference — only update the ones that do.
- **No CHANGELOGs**: This repository no longer maintains per-library CHANGELOG.md files. Do NOT create or update any CHANGELOG files.
- **Source-generated JSON**: Adding new OTel part types requires: (1) new inner class, (2) `[JsonSerializable]` registration on `OtelContext`, (3) switch case in `SerializeChatMessages()`.
- **LoggerMessage text**: When using `[LoggerMessage]`, the message text should match the OTel event name for console logger readability.
- **No orphan constants**: Never add a constant to `OpenTelemetryConsts.cs` unless the same PR also adds at least one emission site for it. If the convention defines an attribute that no current client populates, classify the change as 🟢 *Constant not yet emitted* and defer the constant — do not add it ahead of emission. Verify with `grep -rn NewConstantName src/Libraries/Microsoft.Extensions.AI/` before submitting. This defer rule applies **only** to brand-new attributes/metrics that have no emission site. A change to a convention item the code **already emits** — a type/unit change (e.g. `gen_ai.request.top_k` `double` → `int`), a requiredness/scope change, a rename, a sampling-relevance change, or a new well-known value for an already-emitted attribute — is actionable and must be applied in the same pass, not deferred. Deferral marks individual constants; it is never a reason to skip the overall update.
- **Area-aware constants**: Pick the nested class in `OpenTelemetryConsts.cs` based on the upstream area: `GenAI.*` for `gen-ai/*`, `MCP.*` for `mcp/*`. Provider-specific attributes (`openai.*`, `anthropic.*`, `aws-bedrock.*`, `azure-ai-inference.*`) generally belong in the **provider package's** constants file, not in `Microsoft.Extensions.AI/OpenTelemetryConsts.cs`. See [references/implementation-patterns.md §Area placement guidance](references/implementation-patterns.md#area-placement-guidance).

## Validation

After implementing changes (Modes 2, 4, and 5):

1. **Restore, build, and test** using the commands in [references/build-commands.md](references/build-commands.md) — pick the form (Windows or Linux/macOS) that matches your environment. Always remove any stale `SDK.sln*` files first; they cause build errors when present alongside a newly-generated filtered solution.
2. Verify no new build warnings in `artifacts/log/Build.binlog`
3. If the public API surface changed, regenerate the API baselines per [references/build-commands.md](references/build-commands.md) — then **discard baseline updates for unrelated libraries** (only keep baselines for libraries changed as part of the convention update)
