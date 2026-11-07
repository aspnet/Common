# PR Title and Description Format

When asked to create or update a PR after implementing GenAI semantic-conventions changes, use this guidance.

## Title

When a target **GenAI** version number is known (a release or published schema URL exists):

```text
Update OpenTelemetry GenAI conventions to v{version}
```

When no GenAI version number is determined yet -- the common case while `semantic-conventions-genai` is unreleased (no release and no published schema URL) -- use `latest` in place of a version number:

```text
Update OpenTelemetry GenAI conventions to latest
```

The GenAI version is independent of core semconv (which has its own version line); do not conflate the two, and do not use `versions.env`'s `SEMCONV_VERSION` (that is the core semconv dependency) as the title version. When a version exists, take `{version}` from the `CHANGELOG.md` release header (Towncrier compiles the `changelog.d/` fragments into it at release time) or the published schema URL (`opentelemetry.io/schemas/gen-ai/X.Y.Z`). While unreleased, keep the title as `latest` and identify the update by its `changelog.d/` fragment snapshot (commit / ref / date) in the body instead of a version number. If the PR also includes catch-up work from earlier convention versions or from the consolidated `open-telemetry/semantic-conventions` repo, keep the title focused on the target (the GenAI version or `latest`) and explain the catch-up work in the description.

## Description

The description should include a changes table derived from the audit table and [change-classification.md](change-classification.md). Group or sort rows by GenAI version and include every analyzed change, not only the rows that produced code changes. When no GenAI version number is determined yet, use `latest` in the **Version** column and record the `changelog.d/` fragment snapshot (commit / ref / date) you audited in the description. Use the same red/yellow/green indicators as the classification guide:

- 🟢 for no action required
- 🟡 for minor action required
- 🔴 for code change required

Use this table shape (the **Area** column makes it obvious which package each row targets):

```markdown
| Version | Area | Indicator | Semantic-conventions change | Classification | Compensating change / rationale |
|---|---|:---:|---|---|---|
| vX.Y.Z | `gen-ai` | 🔴 | `gen_ai.example.attribute` added | New required attribute | Added constant, emission, and tests in `{files}`. |
| vX.Y.Z | `gen-ai` | 🟡 | Convention version reference changed | Version bump | Updated OpenTelemetry* doc comments. |
| vX.Y.Z | `gen-ai` | 🟢 | Server-side-only span attribute added | Server-side only | No client-side instrumentation change needed. |
| vX.Y.Z | `openai` | 🔴 | `openai.example.attribute` added | New required attribute | Added to `Microsoft.Extensions.AI.OpenAI` provider package. |
| vX.Y.Z | `mcp` | 🟢 | `mcp.example.attribute` added | N/A — No client | No MCP instrumentation in this repo today. |
```

For each row, describe the compensating change made, or explain why no change was made (already implemented, no local source, no client exists, server-side only, documentation only, etc.).

When the PR includes catch-up work whose source PRs live in the consolidated `open-telemetry/semantic-conventions` repo (earlier `area:gen-ai` work, before these conventions moved), link to those PRs explicitly using the `open-telemetry/semantic-conventions#NNN` form so reviewers can disambiguate them from `semantic-conventions-genai` PR numbers.

Keep release-specific findings in the PR description or implementation summary; do not add them to the skill references unless they are durable cross-release guidance.

## Upstream-scan tracking PR body template

A recurring **upstream-scan tracking PR** records the state of the last upstream scan and lists every merged `Unreleased` change and every open upstream PR with its applicability to this repo. This skill is responsible for producing that PR's full title and body. Assemble the body in the order below, and **keep the machine-readable tracking state at the very bottom** so the human-facing content (what shipped, then the applicability tables) leads.

### 1. Status note (optional)

While `semantic-conventions-genai` is unreleased, lead with a short blockquote noting the PR is draft pending the first release and that both conventions are Development stability:

```markdown
> **Draft** pending the first release of
> [open-telemetry/semantic-conventions-genai](https://github.com/open-telemetry/semantic-conventions-genai).
> Both conventions are **Development** stability and **unreleased**. Merge once they ship in a tagged release.
```

### 2. Release tagging confidence

Include a short release-signal statement near the top of every upstream-scan tracking
PR body. Use the workflow's setup signal as the baseline: GitHub latest release
`tag_name`, resolved through `refs/tags`, with annotated tags dereferenced to their
commit. State the confidence from the skill's release-tagging analysis:

```markdown
Release tagging confidence: `none` -- no published GenAI release signal exists yet.
```

or:

```markdown
Release tagging confidence: `high` -- GitHub latest release `vX.Y.Z` resolves to commit `<sha>`.
```

If the analysis finds a clearly dependable release signal that differs from the
workflow baseline, include this block before the tracking tables:

```markdown
> [!CAUTION]
> Release tagging signal may have changed: <describe the dependable signal found>.
> Suggested follow-up: update <the skill and/or workflow> to use <specific signal> instead of <current signal>.
```

Omit the caution block when no alternate dependable signal was found.

### 3. What this PR implements

A compact table of the convention changes this PR actually implements, with the upstream PR link and the compensating change, followed by a one-line **Validation** summary (build TFMs + warning count, test counts, public-API-surface impact, and the doc-comment version reference state):

```markdown
## What this PR implements

| Area | Convention | Upstream | Compensating change |
|---|---|---|---|
| `gen-ai` | `gen_ai.example.attribute` | [semantic-conventions-genai#NNN](https://github.com/open-telemetry/semantic-conventions-genai/pull/NNN) | Emit on chat spans in `OpenTelemetryChatClient`. |

Validation: build clean (net8.0/net9.0/net10.0/net11.0, 0 warnings); N tests pass. No public API surface change; doc-comment version reference left at `vX.Y`.
```

### 4. Upstream scan tracking tables

Two tables -- merged `Unreleased` changes and in-flight open PRs -- using **one consistent column set** so they read the same way:

```markdown
## Upstream scan tracking

Legend: 🔴 implemented here · ✅ already aligned · 🟡 watch/deferred · 🟢 not applicable (no client / docs-only)

### Merged upstream changes (Unreleased) -- applicability to dotnet/extensions

| Upstream PR | Area | Change | Applicability | Status |
|---|---|---|:---:|---|
| [#NNN](https://github.com/open-telemetry/semantic-conventions-genai/pull/NNN) | `gen-ai` | `gen_ai.example.attribute` | 🔴 | Implemented in `OpenTelemetryChatClient`. |
| [#NNN](https://github.com/open-telemetry/semantic-conventions-genai/pull/NNN) | `gen-ai` | `top_k` type change | ✅ | Already aligned (`ChatOptions.TopK` is `int?`). |

### In-flight upstream changes (open PRs) -- applicability if merged

List **every** open PR in `open-telemetry/semantic-conventions-genai`, each assessed for applicability to this repo using the same columns and indicators as the merged table (dependency / CI / chore PRs are 🟢 *no convention impact*). If there are **no** open PRs, replace the table with the single line `No open PRs.`

| Upstream PR | Area | Change | Applicability | Status |
|---|---|---|:---:|---|
| [#NNN](https://github.com/open-telemetry/semantic-conventions-genai/pull/NNN) | `gen-ai` | `document` modality | 🟡 | Watch (additive message serialization). |
| [#NNN](https://github.com/open-telemetry/semantic-conventions-genai/pull/NNN) | `mcp` | tool.call.arguments opt-in | 🟢 | No MCP instrumentation. |
```

Column rules for both tables:

- Keep the **Applicability** column to the color symbol only, and put the explanatory text in the separate **Status** column. The merged-changes table and the in-flight (open-PR) table share these columns exactly; in the in-flight table, **Status** describes what would change *if the PR merged*.
- Keep the **Change** text wrappable so the table fits the screen: browsers do not break long `/`-separated runs (e.g. `entity/identity/finish_reason/...`), and such a run forces the column -- and the whole table -- wider than the viewport. Write multi-segment descriptions with break opportunities (`', '` or `' / '` with surrounding spaces) so the cell can wrap.

Applicability legend (symbol-only in the Applicability column):

- 🔴 implemented here
- ✅ already aligned (no change needed)
- 🟡 watch / deferred
- 🟢 not applicable (no client / docs-only / other repo)

### 5. Tracking state -- very bottom

Place the machine-readable scan state at the **very bottom** of the body, after the applicability tables. The body **ends with this block** -- do not append a refresh procedure or any other section after it. The state lives in a fenced `yaml` block whose first and last lines are `# meai-otel-genai-worker:state:begin` / `# meai-otel-genai-worker:state:end` sentinel comments so the next run can locate and parse it. The next run matches these as **whole** comment lines (leading indent and a trailing CR are tolerated, but the `# ` prefix and the `:state:begin`/`:state:end` suffix must be exact), so keep each sentinel on its own line. Keep both sentinels inside the code fence as `yaml` comments: GitHub Actions safe-output processing preserves fenced content, so `yaml`-comment delimiters survive round-trips and stay parseable:

````markdown
## Tracking state

```yaml
# meai-otel-genai-worker:state:begin
upstream-repo: open-telemetry/semantic-conventions-genai
upstream-scan-ref: <commit-sha>              # optional inline note on what changed since the prior ref
upstream-scan-date: <ISO-8601 UTC>
upstream-release: none                       # Unreleased; Towncrier fragments under changelog.d/
core-semconv-dependency: vX.Y.Z              # versions.env SEMCONV_VERSION (core dep, NOT the GenAI version)
dotnet-extensions-implemented-version: vX.Y  # doc-comment version reference currently in source
feedback-processed-through: <ISO-8601 UTC>   # watermark: run start when PR review feedback was last processed
# meai-otel-genai-worker:state:end
```
````
