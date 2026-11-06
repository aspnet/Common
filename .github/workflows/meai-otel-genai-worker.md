---
name: "MEAI: Otel GenAI Worker"
description: >-
  Produce or refresh the single draft pull request that integrates OpenTelemetry
  gen-ai semantic-conventions updates into Microsoft.Extensions.AI. Invoked per-target
  by the MEAI Otel GenAI Orchestrator (workflow_call) or manually (workflow_dispatch).

permissions:
  contents: read
  pull-requests: read
  issues: read
  actions: read

safe-outputs:
  # Threat detection screens the untrusted upstream conventions and reviewer feedback this
  # agent integrates. It runs in a separate gh-aw job that selects its Copilot PAT from the
  # pool, the same as the agent job.
  threat-detection:
    engine:
      id: copilot
      env:
        COPILOT_GITHUB_TOKEN: ${{ case(needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0, needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1, needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2, needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3, needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4, needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5, needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6, needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7, needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8, needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9, 'NO COPILOT PAT AVAILABLE') }}
  create-pull-request:
    draft: true
    labels: [automation, area-ai]
    base-branch: main
    preserve-branch-name: true
    if-no-changes: "warn"
    allowed-files:
      - "src/Libraries/Microsoft.Extensions.AI*/**"
      - "test/Libraries/Microsoft.Extensions.AI*/**"
  push-to-pull-request-branch:
    target: "*"
    required-labels: [automation, area-ai]
    if-no-changes: "ignore"
    allowed-files:
      - "src/Libraries/Microsoft.Extensions.AI*/**"
      - "test/Libraries/Microsoft.Extensions.AI*/**"
  update-pull-request:
    target: "*"
    required-labels: [automation, area-ai]
    title: true
    body: true
  add-comment:
    target: "*"
    required-labels: [automation, area-ai]
    max: 1
  mark-pull-request-as-ready-for-review:
    target: "*"
    required-labels: [automation, area-ai]
  noop:
    report-as-issue: false
  report-failure-as-issue: false

network:
  allowed:
    - defaults
    - dotnet
    - github
    - opentelemetry.io
    - "*.opentelemetry.io"
    - "*.azureedge.net"

tools:
  # Route Safe Outputs through the CLI proxy instead of the native HTTP MCP endpoint on the
  # awmg-mcpg gateway, which the firewall denies (TCP_DENIED/403) before recommending the
  # non-compilable `awmgmcpg` network.allowed entry. Workaround for github/gh-aw#45915;
  # github.mode: gh-proxy (below) removes the native GitHub MCP endpoint for the same reason.
  cli-proxy: true
  github:
    mode: gh-proxy
    toolsets: [default]
    # Only content at "approved" integrity or higher reaches the agent. Write-access
    # authors (OWNER/MEMBER/COLLABORATOR) are approved automatically; feedback from
    # anyone else is filtered out unless a maintainer endorses it (see integrity-reactions),
    # which promotes that item to approved. This replaces any hand-rolled trust handling.
    min-integrity: approved

features:
  # Let a maintainer promote an external contributor's comment to "approved" by adding an
  # endorsement reaction (👍/❤️), so the agent then sees and can act on it. Requires
  # gh-proxy mode (above) to attribute the reacting user.
  integrity-reactions: true

runs-on: ubuntu-latest
timeout-minutes: 350

checkout:
  fetch: ["*"]
  fetch-depth: 0

concurrency:
  group: meai-otel-genai-worker
  cancel-in-progress: false

on:
  # Invoked per-target by the MEAI Otel GenAI Orchestrator as a reusable
  # workflow so this runs in the orchestrator's run context and inherits its actor,
  # satisfying gh-aw activation's role check.
  workflow_call:
    inputs:
      target:
        description: "Single discovery target (JSON) from meai-otel-genai-orchestrator-discover.sh."
        required: true
        type: string
  # Manual dispatch for standalone testing: provide a target JSON, or just an
  # upstream_ref (the setup resolves the rest), or nothing (scan default-branch HEAD).
  workflow_dispatch:
    inputs:
      target:
        description: "Single discovery target (JSON). Leave empty to resolve from upstream_ref."
        required: false
        type: string
      upstream_ref:
        description: >-
          Optional git ref (branch, tag, or commit SHA) in
          open-telemetry/semantic-conventions-genai to scan instead of the
          default-branch HEAD. Leave empty to scan the default-branch HEAD.
        required: false
        type: string
  permissions: {}

# Before the agent runs, deterministically prepare the run: resolve the upstream scan
# target (from the orchestrator's `target` input, or a standalone dispatch's upstream_ref /
# default HEAD), discover and classify the maintained draft PR (ours / adopt / blocked /
# none), and compute the recommended lifecycle action. The agent consumes target.json
# instead of discovering that state itself, and stamps target.json's run_started_at back as
# the new feedback-processed-through watermark. target.json is uploaded in the agent artifact.
steps:
  - name: Set up the run context
    env:
      GH_TOKEN: ${{ github.token }}
      TARGET_JSON: ${{ inputs.target }}
      UPSTREAM_REF: ${{ inputs.upstream_ref }}
    run: bash .github/scripts/meai-otel-genai-worker-setup.sh

# After the agent runs, verify each full-body pull-request write still carries the state
# block that the next run reads back to resume: the tracking block, a non-empty
# upstream-scan-ref, and a feedback-processed-through watermark. A PR/update body missing
# any of these would leave the next run unable to recover its place, so fail before it can publish.
post-steps:
  - name: Validate agent output identity
    run: |
      set -euo pipefail
      mkdir -p /tmp/gh-aw/agent
      out=/tmp/gh-aw/agent_output.json
      if [ ! -f "$out" ]; then
        echo "::notice::No agent output to validate"; exit 0
      fi
      # Only full-body PR writes carry the tracking block. Skip append/prepend updates
      # (partial bodies) and non-PR items (comments, no-op).
      idx=$(jq -r '(.items // []) | to_entries[]
        | select(.value.type=="create_pull_request"
            or (.value.type=="update_pull_request"
                and ((.value.operation // "replace")=="replace")))
        | select((.value.body // "") != "")
        | .key' "$out" 2>/dev/null || true)
      if [ -z "$idx" ]; then
        echo "::notice::No full-body PR-writing items -- nothing to validate"; exit 0
      fi
      rc=0
      while IFS= read -r i; do
        [ -n "$i" ] || continue
        typ=$(jq -r ".items[$i].type" "$out")
        body=$(jq -r ".items[$i].body" "$out")
        miss=""
        # Validate the state block the same way the next run's setup script reads it: the block
        # is delimited by whole `# meai-otel-genai-worker:state:begin` / `:state:end` comment
        # lines (leading indent / trailing CR tolerated, "# " prefix and ":state:" suffix exact),
        # and the required fields are extracted anchored-to-line-start WITHIN that block (the LAST
        # begin..end range, since the body ends with it). Matching the reader exactly means
        # anything this guard admits is recoverable next run; a body missing the markers or a
        # field would parse to empty state and wedge the machine into a re-produce loop.
        blk=$(printf '%s' "$body" | awk '{t=$0;sub(/\r$/,"",t);gsub(/^[[:space:]]+|[[:space:]]+$/,"",t)} t=="# meai-otel-genai-worker:state:begin"{inb=1;buf=$0 ORS;next} inb{buf=buf $0 ORS; if(t=="# meai-otel-genai-worker:state:end"){inb=0;last=buf}} END{if(inb)last=buf; printf "%s", last}')
        printf '%s\n' "$body" | awk '{t=$0;sub(/\r$/,"",t);gsub(/^[[:space:]]+|[[:space:]]+$/,"",t)} t=="# meai-otel-genai-worker:state:begin"{f=1} END{exit !f}' || miss="$miss begin-marker"
        printf '%s\n' "$body" | awk '{t=$0;sub(/\r$/,"",t);gsub(/^[[:space:]]+|[[:space:]]+$/,"",t)} t=="# meai-otel-genai-worker:state:end"{f=1} END{exit !f}' || miss="$miss end-marker"
        printf '%s' "$blk" | grep -Eq '^[[:space:]]*[-*+>]*[[:space:]]*upstream-scan-ref:[[:space:]]*"?[0-9a-fA-F]{7,}' || miss="$miss upstream-scan-ref"
        printf '%s' "$blk" | grep -Eq '^[[:space:]]*[-*+>]*[[:space:]]*feedback-processed-through:[[:space:]]*"?[^[:space:]"]' || miss="$miss feedback-processed-through"
        if [ -n "$miss" ]; then
          echo "::error::$typ item #$i is missing required tracking identity:$miss"
          rc=1
        fi
      done <<< "$idx"
      if [ "$rc" -ne 0 ]; then
        # The generated safe-output job intentionally runs after failed agents to report
        # valid partial outputs. Quarantine this invalid mutation so it cannot be published.
        cp "$out" /tmp/gh-aw/agent/rejected_agent_output.json
        printf '%s\n' '{"items":[]}' > "$out"
        echo "::error::Rejected invalid agent output; downstream safe outputs will receive no items."
        exit "$rc"
      fi
      echo "Agent output identity validated."

# ###############################################################
# Select a PAT from the pool and override COPILOT_GITHUB_TOKEN.
# Run agentic jobs in an isolated `copilot-pat-pool` environment.
#
# When org-level billing is available, this will be removed.
# See `shared/pat_pool.README.md` for more information.
# ###############################################################
imports:
  - uses: shared/pat_pool.md
    with:
      environment: copilot-pat-pool

environment: copilot-pat-pool

engine:
  id: copilot
  env:
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0, needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1, needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2, needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3, needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4, needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5, needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6, needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7, needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8, needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9, 'NO COPILOT PAT AVAILABLE') }}
---

# MEAI: Otel GenAI Worker

## Goal

Keep `Microsoft.Extensions.AI` aligned with the OpenTelemetry gen-ai semantic
conventions by continuously maintaining a **single draft pull request** against
`main` in this repository. Each daily run compares the upstream
`open-telemetry/semantic-conventions-genai` repository against `main`, produces or
refreshes an integration plan, implements it, validates it, and keeps the PR up to
date until the upstream release is published -- at which point the PR is marked
**Ready for Review**.

**The gen-ai conventions being unreleased is the normal, expected steady state.**
These conventions live in `Development` stability and are integrated from merged
upstream commits *before* any tagged release exists. The draft PR is built and
maintained from those unreleased upstream changes -- the absence of a published
release is **never** a reason to skip work. Publishing a release only flips the
finished draft PR to Ready for Review (Step 6); it does not gate whether the
integration happens. Do **not** no-op merely because `upstream-release` is `none`.

The repository skill `update-otel-genai-conventions` (in `.github/skills/`) is the
authority for **how** to analyze conventions and implement changes. This workflow
governs **lifecycle/idempotency** (which PR to touch and what state to leave it in).

## Step 0 -- Read the run context (authoritative)

Before you do anything, read the run context the host setup wrote to
`/tmp/gh-aw/agent/target.json` (the authoritative input for this run -- do not
re-discover this state yourself):

- **`target.json`** -- the resolved scan target and PR discovery:
  - `upstream_repo`, `upstream_ref`, `upstream_sha` -- the exact upstream commit to
    scan (already resolved; use `upstream_sha` as `upstream-scan-ref`, do not re-resolve).
  - `upstream_release` -- the latest published gen-ai release tag, or `none`.
  - `upstream_release_commit` -- the commit reached by resolving `upstream_release`
    through `refs/tags`, including annotated-tag dereferencing, or empty when no
    release commit could be resolved.
  - `release_matches_scan` -- `true` when `upstream_release_commit` equals the
    current `upstream_sha` being scanned.
  - `release_matches_pr` / `release_ready` -- `true` only when the latest release tag
    resolves to the maintained PR's recorded `upstream-scan-ref`. This is the only
    release signal allowed to route an existing draft PR to Step 6.
  - `release_detection_confidence`, `release_detection_signal` -- the setup's
    confidence and evidence for how the latest release tag was identified.
  - `desired_branch` -- the evergreen canonical PR branch name
    (`update-otel-genai-to-latest`).
  - `pr_branch` -- the actual head branch of the maintained PR, or empty when none
    exists. This can be `desired_branch` or a run-suffixed branch created when the
    canonical branch already existed but no open PR used it.
  - `pr` -- the maintained PR number, or empty when none exists.
  - `pr_state`, `pr_is_draft`, `pr_recorded_sha`, `pr_recorded_release` -- the
    discovered PR's state and the `upstream-scan-ref` / `upstream-release` it records.
  - `watermark` -- the PR body's current `feedback-processed-through` value (empty on a
    fresh PR, or when the host could not read the body -- see "Recover an unread body
    first" below); the cutoff for which review feedback is new this run (see "Honoring
    reviewer feedback").
  - `run_started_at` -- this run's start time, captured before the run began. Stamp this
    back as the new `feedback-processed-through` watermark in the tracking block (Step 5).
  - `has_new_feedback` -- `true` when the setup's author-agnostic wake gate saw review
    activity newer than `watermark` on the maintained PR (it never reads comment bodies).
    When the PR is caught up on the upstream SHA but this is `true`, run the feedback-only
    pass in Step 3 instead of no-opping, so the watermark advances and the wake clears.
  - `classification` -- one of:
    - `ours` -- an open `automation`+`area-ai` PR on `pr_branch` that already carries
      our `meai-otel-genai-worker:state` block. Maintain it.
    - `adopt` -- an open `automation`+`area-ai` PR on `desired_branch` that a human
      **bootstrapped** but that has **no** tracking block yet. **Take it over**:
      treat it like an incremental update, and write the full tracking block into its
      body this run so future runs classify it as `ours`.
    - `blocked` -- a PR occupies `desired_branch` but is **not** our automation PR (a
      human owns it). **Stand down**: emit a `noop` explaining a human-owned PR holds
      the branch, and make no other output.
    - `none` -- no open canonical PR and no valid open suffixed tracking PR; the fresh
      path applies.
  - `action` -- the setup's recommended lifecycle action (`produce` or `noop`). When
    it is `noop` **and** `classification` is `ours` (caught up on the SHA, no new review
    activity, and no release action is pending), you may **early-exit**: emit a single `noop` with the
    reason and do **not** run the expensive build. For every other case run the full
    analysis and let your Step 3 decision be authoritative -- `action` is only a hint.

**Recover an unread body first.** The host reads `pr_recorded_sha` and `watermark`
from the maintained PR body, so a transient fetch failure can leave both empty even
though `pr` is non-empty. When `classification` is `ours` but `pr_recorded_sha` (and
usually `watermark`) is empty, do **not** treat the PR as fresh: fetch the PR body
yourself once, locate its `# meai-otel-genai-worker:state:begin` .. `:state:end`
block, and read `upstream-scan-ref` and `feedback-processed-through` from it before
scoping feedback ("Honoring reviewer feedback") or computing the differential. If your
own read also fails, treat `feedback-processed-through` as this run's `run_started_at`
(process no prior feedback this run) rather than reprocessing every earlier comment --
the next run recovers the real watermark and picks up anything genuinely new.

If `classification` is `blocked`, stop now with a `noop`. If `action` is `noop` and
`classification` is `ours`, stop now with a `noop`. Otherwise continue.

## Step 1 -- Capture the upstream state

**Which upstream ref to scan.** The host setup already resolved this into
`target.json`: scan `upstream_sha` (the concrete commit for the orchestrator's target, a
standalone `upstream_ref` dispatch, or the default-branch HEAD). Record `upstream_sha`
as `upstream-scan-ref`; all downstream logic (the SHA comparison in Step 3, the
tracking block) uses that resolved SHA. Do **not** re-resolve the ref yourself.

1. Read the state of `open-telemetry/semantic-conventions-genai` at the scanned ref
   (the `upstream_ref` override when provided, otherwise its default branch):
   - The scanned ref's **commit SHA** (`upstream-scan-ref`).
   - Whether a tagged **release** exists/is published (`upstream-release`) and the
     setup's resolved `upstream_release_commit` / `release_detection_confidence`.
     Record `none` while the conventions are still unreleased (Development stability).
     A value of `none` is the expected normal case and must **not** cause a no-op or
     short-circuit -- continue to Step 2 and integrate the unreleased changes.
   - The **core semantic-conventions dependency version** the gen-ai conventions
     target (`core-semconv-dependency`).
2. Derive the **naming token** `{target}` for the branch and title:
   - If a gen-ai release is published **and** `target.json.release_matches_scan` is
     true, use that release version with a `v` prefix (e.g. `v1.42.0`).
   - Otherwise -- the normal case while the conventions are unreleased -- use the
     literal token `latest`. Do **not** substitute the core semantic-conventions
     dependency version (`core-semconv-dependency`) here; that is the core semconv
     dependency, **not** the gen-ai conventions version. Identify the unreleased
     update by its upstream commit SHA and scan date in the body instead.
   - The branch name is `update-otel-genai-to-{target}` and the PR title is
     `Update open-telemetry/semantic-conventions-genai to {target}` (e.g.
     `...to latest` while unreleased, `...to v1.42.0` once a release is published).
     The title carries **no** `[automation]` prefix; the `automation` label conveys
     that instead.

## Step 2 -- Build the plan via the skill

Invoke the `update-otel-genai-conventions` skill for its analysis only -- build the
plan **in working memory**. Because this is an unattended scheduled run, do **not**
use the skill's interactive Plan-then-Implement checkpoint: do **not** write a
`plan.md` file and do **not** pause for human approval between analysis and
implementation. Drive the work from the audit table and the ordered work-item list the
skill produces, then implement directly in Step 4.

Do the **full** convention cross-reference -- compare the gen-ai attributes,
metrics, events, and operation names defined at the scanned upstream commit against what the
code actually emits (the implemented version from the doc comments). Drive this from
the conventions themselves, not from the upstream CHANGELOG: a `Development`-stability
upstream commonly shows `Unreleased`/no tag while still carrying merged convention
changes that are ahead of the implemented version. An empty CHANGELOG section or the
lack of a release tag is **not** evidence that there is nothing to integrate -- only
a completed cross-reference that finds zero deltas vs. the implemented version is.

Note: the skill's own "existing PR preflight" tells it to stop when an open PR
exists. **Override that for this workflow** -- do not stop; instead follow the
PR-handling decision in Step 3. Use the skill purely for the analysis, change
classification, implementation patterns, testing guidance, and validation commands.

Also ask the skill for its **release-tagging analysis**. Use the setup's
`release_detection_*` fields as the baseline release signal. The skill should compare
the observed upstream release signals (GitHub latest release tag, resolved tag commit,
CHANGELOG release headers, schema URL, and any durable tag naming convention it can
verify) and report a confidence level. If the skill finds a clearly dependable release
signal that differs from the setup's GitHub-release tag dereferencing, preserve that
finding for the PR body caution block in Step 5; do not change the Step 3 release gate
inside the run.

### What counts as work -- track merged upstream changes aggressively

This workflow tracks **merged upstream gen-ai convention changes regardless of
whether a release is published.** Each merged upstream PR (e.g. every
`changelog.d/*.md` fragment, or any convention change present at the scanned upstream commit
but not yet reflected in the implemented version) is a tracked change. Treat the
set of such changes ahead of the implemented version as the work to integrate.

Classify each tracked change as **actionable now** or **deferred**, and do **not**
let deferral collapse the run into a no-op:

- **Actionable now (must be implemented in this run):** any change that touches a
  convention item the code **already emits** -- a type/unit change (e.g.
  `gen_ai.request.top_k` `double` -> `int`), a requiredness or scope change, a
  rename, a sampling-relevance change, a new well-known value for an
  already-emitted attribute (e.g. a new `gen_ai.provider.name` value), or a new
  attribute/metric that maps onto a capability M.E.AI already instruments. These
  are integrated immediately; they are never deferred.
- **Deferred (tracked, not coded yet):** a brand-new attribute/metric/event/span
  with **no** current emission site, or a capability M.E.AI does not yet instrument
  (e.g. memory, retrieval, workflow, agent-framework spans), or a documentation-only
  clarification with no code impact. The skill's "no-orphan-constants" rule means
  you do **not** add the constant yet -- but you **still** record the item in the
  PR's changes table (🟢) so the draft PR documents the full upstream delta.

The "defer" classification applies to **individual constants**, never to the run as
a whole. As long as **one or more** tracked changes exist ahead of the implemented
version, there is a non-zero delta: open or maintain the draft PR (Step 3/4),
implement every actionable-now item, and document every deferred item in the changes
table. A run only no-ops when there are genuinely **zero** unintegrated upstream
gen-ai changes, or the tracked PR is already caught up on the upstream SHA (Step 3).

## Step 3 -- Choose the action from the setup's discovery

The host setup already discovered the maintained PR and recorded it in `target.json`
(Step 0) -- do **not** re-list or bulk-fetch PR bodies yourself. Use its fields:

- `classification` = `none` -> no open PR on `desired_branch`, and no valid open
  suffixed tracking PR discovered by the setup: the **Fresh PR** path.
  For a nicer body you may do **one** lightweight lookup of a prior *closed/merged*
  `automation`+`area-ai` PR on `update-otel-genai-to-latest` to reference (query only
  number/state/mergedAt -- never bulk-fetch bodies); if the lookup is unavailable, skip
  the reference rather than failing.
- `classification` = `blocked` -> a human owns a PR on `desired_branch`: emit a `noop`
  saying so and make no other output.
- `classification` = `ours` or `adopt` -> compare `pr_recorded_sha` to `upstream_sha`
  and pick the row below. For `adopt`, additionally write the **full tracking block**
  into the body this run (the bootstrapped PR has none yet) so it becomes `ours`.
  When you need to fetch, check out, or push the maintained PR branch, use
  `target.json.pr_branch` (the actual branch), not `target.json.desired_branch`
  (the canonical branch).

First check the **release gate**: if `target.json.release_ready` is `true` and the PR
has a pending release action, go straight to **Step 6** regardless of the current
`upstream_sha` comparison. A release action is pending when the PR is still draft, or
when its tracking block's `upstream-release` does not equal `target.json.upstream_release`.
`release_ready` means the latest published release tag resolved to the same commit
recorded in the PR's `upstream-scan-ref`; do **not** use `upstream_release != none` as
the gate.

Otherwise compare `pr_recorded_sha` to `upstream_sha` and pick the matching action. The
SHA comparison is the primary decision; the PR's draft state only matters when behind.

| If the maintained PR is... | ...and it is | Action |
|---|---|---|
| caught up with the upstream SHA | open (draft or not) **or** merged, `has_new_feedback` = false | **No-op** -- no comment, report, issue, or PR; write the reason to the step summary (see no-op rules). (The release gate above already diverted any pending release action to Step 6, and the feedback-only row below handles new review activity, so this row is reached only when no release title/body or mark-ready transition is pending and there is no new review activity.) |
| caught up with the upstream SHA | open (draft or not) with `has_new_feedback` = true | **Feedback-only update.** Run the reviewer-feedback pass (see "Honoring reviewer feedback"): address the approved feedback, push any resulting commit(s), refresh the PR body/tracking block, advance `feedback-processed-through` to `run_started_at`, and post the single summary comment. If the pass finds nothing actionable, still refresh the body so the watermark advances and the wake does not repeat. |
| **behind** the upstream SHA | open **draft** (`ours` or `adopt`) | **Incremental update.** Re-analyze against `main` plus what the branch already integrates; push one batch of commit(s) to the PR branch; refresh the PR body/tracking block (for `adopt`, add the full tracking block); comment summarizing the delta. |
| **behind** the upstream SHA | open **non-draft** | **Advisory only -- do not implement.** Comment capturing the additional upstream changes to consider; note that re-marking the PR as draft lets the next scheduled run implement them, and that the workflow can be dispatched manually to run immediately. |
| `classification` = `none`, or a prior PR is **closed without merging** / **merged-but-behind** | absent, closed, or merged-but-behind | **Fresh PR** (Step 4). For a merged-but-behind PR you referenced, describe the updates layered on top. |

If the situation does not cleanly match a row, use judgment toward the overall goal:
**keep one draft PR continuously updated until the upstream release publishes.**

Once a row other than No-op is selected (Fresh PR, Incremental, or Advisory),
proceed to carry it out. Do **not** re-derive a no-op afterward from the CHANGELOG
state or from `upstream-release: none` -- the work decision is driven by the SHA
comparison and the zero-delta check from Step 2. A *published* release never causes a
no-op by itself; only `release_ready` can escalate a pending release action into Step 6
(handled by the release gate above).

## Step 4 -- Implement

Follow the skill's **Implementation Procedure** for each work item. Then validate.

### File scope -- what this workflow may change

The pull request may modify **only** files under these paths (the safe output
enforces this as an allow-list, and the run fails if the patch touches anything
else):
- `src/Libraries/Microsoft.Extensions.AI*/**`
- `test/Libraries/Microsoft.Extensions.AI*/**`

**Never** edit, stage, or commit anything outside that set. In particular do **not**
touch: this workflow and its generated lock (`.github/**`, including
`.github/workflows/meai-otel-genai-orchestrator.*`, `.github/workflows/meai-otel-genai-worker.*`,
`.github/scripts/meai-otel-genai-*`, and `.github/aw/**`), `global.json`,
`NuGet.config`, `Directory.Packages.props`, any `*.sln`/`SDK.sln*` solution files,
or dependency lockfiles. If a build step generates such files (e.g. `SDK.sln`),
delete them before producing output so they cannot leak into the patch.

### How to produce the patch (critical -- avoids fork base-resolution failures)

The patch the safe output turns into a PR is generated by diffing your commits
against the **exact commit that was checked out** (`GITHUB_SHA`). This stays clean
only if `HEAD` never **switches** onto another branch. Do **not** run
`git checkout -b update-otel-genai-to-{target}` or `git switch -c ...`: switching
branches makes patch generation fall back to a `merge-base` against the remote
default branch, which on a fork sweeps the entire fork divergence into the patch
(hundreds of unrelated files) and fails the run. Creating a branch **ref** that
points at the current commit without switching to it is safe (and required -- see
below). So:

- **Fresh path** (merged-but-behind, closed, or no PR): the run is checked out **on the
  base branch** (`main`), so committing now would advance `main` itself and leave the PR
  branch you create pointing at the **same commit as its base** -- the generated patch is
  then **empty** (and, with threat-detection enabled, the run hard-fails because no
  patch/bundle is produced for the detection job to screen). The setup step has already
  **detached `HEAD`** at the checked-out commit for you, so your commit advances a detached
  `HEAD` and leaves `main` where it is -- you do not need to run any `git checkout` yourself.
  Do **not** switch onto a branch with `git checkout`/`git checkout -b`/`git switch -c`
  (switching blows up patch generation on a fork), and do **not** run `git fetch`,
  `git reset`, `git rebase`, or `git merge`. Make your edits,
  delete any generated solution/build artifacts (`SDK.sln*`, `artifacts/`) so they
  cannot leak, then stage **only** the in-scope paths. If the scan produced code or
  documentation edits, commit them as a single commit on the current `HEAD`:
  `git add -- src/Libraries/Microsoft.Extensions.AI* test/Libraries/Microsoft.Extensions.AI* && if ! git diff --cached --quiet; then git commit -m "Update open-telemetry/semantic-conventions-genai to {target}"; fi`.
  If every tracked upstream change is deferred and there are **no** staged file changes,
  there is nothing to integrate yet: do **not** invent a placeholder file, an empty commit,
  or an empty PR, and do **not** call `create-pull-request`. An empty (patchless) PR cannot
  be threat-screened and would ship a content-free branch, so `create-pull-request` no longer
  permits empty branches. Emit a `noop` safe output instead and stop; the tracking PR is
  opened on the first run that integrates a real change.
  Now choose the PR branch name. The canonical name is `desired_branch` from
  `target.json` (`update-otel-genai-to-latest`). Before using it, check whether a branch
  with that name already exists on the remote -- a stale branch can linger from a
  previously closed PR:
  `git ls-remote --heads origin "$DESIRED_BRANCH"` (where `$DESIRED_BRANCH` is
  `target.json`'s `desired_branch`).
  - If the remote has **no** such branch, use `desired_branch`.
  - If the branch **already exists**, do **not** overwrite, delete, or force-recreate
    it. Instead suffix the name with the current workflow run id (the `$GITHUB_RUN_ID`
    environment variable) as `{desired_branch}_{run_id}`, and remember
    that you deviated so Step 5 can record it in the PR body.
  Call the chosen name `{branch}`. Create the local branch **ref** for `{branch}`
  pointing at that commit **without switching to it** -- the `create-pull-request`
  safe output pins this ref to build the bundle and fails with
  `Needed a single revision` if it is absent:
  `git branch {branch} HEAD`.
  Because `main` still points at the checked-out commit while `{branch}` points at your
  commit on top of it, the generated patch contains **only** your commit.
  Then emit a `create-pull-request` safe output with:
  - branch `{branch}` (the ref you just created -- the safe output pushes it to the
    remote),
  - title `Update open-telemetry/semantic-conventions-genai to {target}`,
  - the body described in Step 5,
  - draft state (configured by the safe output).
- **Incremental path** (behind open draft, `ours` or `adopt`): the PR branch already
  exists on the remote (`target.json`'s `pr_branch`), so here you **do** fetch and
  check out that existing branch
  (`PR_BRANCH="$(jq -r '.pr_branch' /tmp/gh-aw/agent/target.json)" && git fetch origin "$PR_BRANCH" && git checkout "$PR_BRANCH"`),
  apply only the differential work items on top of what is already integrated, stage
  the in-scope paths, and commit one batch of one or more **new** commits on top of the
  branch's current tip. Every update **appends** to the branch -- never `git commit --amend`,
  `git rebase`, `git reset`, squash, or otherwise rewrite commits already on the PR branch,
  and never force-push -- so the maintained branch keeps a stable, growing trail of
  incremental commits. Then emit a
  `push-to-pull-request-branch` safe output targeting that PR, an `update-pull-request`
  that fully regenerates the body to reflect the current state (see Step 5 -- a
  full-body replace, including the refreshed tracking block; for an `adopt` PR this
  also adds the full tracking block the bootstrapped PR was missing), and a single
  `add-comment` summarizing the delta.

When the skill clones or fetches the upstream `semantic-conventions` repository for
analysis, do it **outside** this repository's working tree (e.g. under `/tmp`), never
inside the checkout, so upstream files never enter the patch.

For **both** implementation paths:
- The build must remain clean (no new warnings) and tests must pass. Use the skill's
  build/test commands (Linux/macOS form). Run a full `./build.sh -vs AI` restore,
  then `./build.sh -build -test`; remove any stale `SDK.sln*` first.
- If restore/build **cannot run** because the internal Azure DevOps feeds are
  unreachable (e.g. `pkgs.dev.azure.com` returns 401/403, or no `project.assets.json`
  is produced), do **not** fall back to a manual review and do **not** open or update a
  PR with code you could not compile. Treat it as a hard failure: emit a
  `report_incomplete` safe output whose `reason` states that the internal NuGet feeds
  were unreachable and the change could not be built or tested, and emit **no**
  `create-pull-request`, `push-to-pull-request-branch`, `update-pull-request`,
  `add-comment`, or `noop` output. The `report_incomplete` signal fails the workflow
  run so the outage is surfaced for investigation instead of shipping unvalidated code.
- Ensure **sufficient test coverage** for every new attribute/metric/emission --
  augment existing tests where possible rather than adding parallel test methods.
- If the public API surface changed, regenerate API baselines and keep only the
  baseline updates for the libraries actually changed.
- Review the result thoroughly against the skill's review checklist before emitting
  output.

### Honoring reviewer feedback on the maintained draft PR

Whenever a matching open draft PR exists and there is new review activity
(`target.json.has_new_feedback` = true) -- on the incremental path (behind the SHA) or on
the feedback-only path (caught up on the SHA) -- address reviewer feedback on that PR as
part of this run, before you commit any differential update.

**Trust is handled by the framework, not by you.** This workflow runs under GitHub
integrity filtering (`min-integrity: approved`): the only PR reviews and review comments
your GitHub tools can see are those from write-access reviewers
(`OWNER`/`MEMBER`/`COLLABORATOR`) plus any external comment a maintainer has explicitly
endorsed with a 👍/❤️ reaction (which promotes that item to approved). Feedback from anyone
else is filtered out before it reaches you. So treat **every** review comment you can read
as trusted reviewer guidance -- you do not need to sanitize it, infer its author's trust
level, or reason about prompt-injection. Integrity filtering governs **who** may give you
feedback; the scope limits below still govern **what** you may act on, so an authorized
comment is still rejected when it asks for out-of-scope work. If you never see a comment,
it was not endorsed; do not go looking for it or try to work around the filter.

- **Collect the feedback.** Use your GitHub tools to read the maintained PR's submitted
  reviews, inline review-comment threads, and standard PR timeline comments (e.g. list
  the pull request's reviews, review comments, and issue comments for `pr`). Consider
  all of these as review feedback.
- **Scope to what is new.** `target.json.watermark` is the PR body's current
  `feedback-processed-through` value. Act only on review feedback **created after** that
  watermark; read anything at or before it for context only (e.g. to understand a terse
  follow-up that builds on an earlier comment), and do not re-process it. Ignore your
  own summary comments and any other bot comments. On a genuinely fresh PR the watermark
  is empty and there is no prior feedback; but an empty `watermark` on an existing PR
  (`pr` non-empty) means the host could not read the body -- recover it as described in
  Step 0 rather than treating all prior feedback as new. (An endorsement that promotes an
  external comment older than the watermark will not by itself reopen it; if a maintainer
  wants such a comment addressed, they can leave a fresh comment.)
- **Settle contradictions.** When two new comments conflict, respect the **most recent**
  guidance (the larger created timestamp) and ignore the superseded direction.
- **Reject any feedback that expands the scope** beyond maintaining the gen-ai
  semantic-conventions integration -- e.g. requests to refactor unrelated code, add
  unrelated features, or modify files outside the allowed paths. Do not act on
  out-of-scope requests; briefly note in the summary comment that they are out of scope
  for this automation.
- Fold the surviving, in-scope feedback into the **same batch of new commit(s)** you append
  for the differential update this run -- do not amend or rewrite commits already on the PR
  branch -- and acknowledge what you addressed versus rejected in the single `add-comment`
  summary.
- **Advance the watermark.** In the refreshed tracking block (Step 5), set
  `feedback-processed-through` to `target.json.run_started_at` (this run's start time,
  captured before the run began). This is the durable, cross-run dedup signal -- the next
  run only reconsiders feedback created after it, so this run's feedback is never
  re-processed, even the non-actionable or out-of-scope items. Any comment that arrives
  while this run is executing carries a later timestamp and is therefore picked up by the
  next run rather than skipped. Advance the watermark to `run_started_at` even when there
  was no actionable feedback this run.

This feedback pass is schedule-driven -- it runs as part of the normal daily incremental
update, picking up review feedback left since the previous run. Do **not** add a
`pull_request_review` (or other review) trigger; reacting to review events directly is out
of scope for this workflow.

## Step 5 -- PR body and tracking block

Write the PR body following the skill's PR-description guidance: a changes table
covering **every** analyzed gen-ai change (not just those producing code changes),
grouped by version, using 🟢/🟡/🔴 indicators with the compensating change or
rationale for each.

**Refresh the description on every iterative update.** On any run that **writes** to an
existing maintained PR -- an incremental update to an open **draft** (behind the SHA), a
feedback-only update (caught up on the SHA with new review activity), or the release
mark-ready in Step 6 -- fully **regenerate** the PR description via `update-pull-request`
(a full-body replace, never an append) so it always reflects the **current** state of the
integration: the cumulative changes table for everything integrated on the branch so
far (not only this run's delta), the current release/confidence note, and the
refreshed tracking block below carrying this run's scan ref, scan date, release, and
`feedback-processed-through` watermark. Refresh the body even when this run's code
delta is small or empty (for example a feedback-only run that only advances the
watermark, or a run that addressed only out-of-scope feedback): a reader opening the PR
after any run must see an accurate, up-to-date description, so leaving stale details
from a prior run is a defect. The **advisory** path (behind the SHA on a **non-draft**
PR, Step 3) is the exception: it only posts a comment and must **not** update or
regenerate the body, so a PR a human has taken into review is left untouched.

If you had to suffix the PR branch name with the run id because the canonical
`update-otel-genai-to-{target}` branch already existed on the remote (see Step 4's
fresh path), add a `> [!NOTE]` block near the top of the PR body stating that the
canonical branch name was already in use by a lingering branch, so this PR uses the
`update-otel-genai-to-{target}_{run_id}` branch instead. Omit the block entirely when
the canonical name was used.

Include the skill's release-tagging confidence in the PR body. If the setup's
`release_detection_confidence` is `none`, say no published release signal exists yet.
If it is `high`, state that the workflow resolved the GitHub latest release tag to
`target.json.upstream_release_commit` and whether it matches the current scan and/or
the maintained PR. If the skill found a different release-tagging approach that is
clearly dependable, add this block near the top of the body, before the tracking tables:

```markdown
> [!CAUTION]
> Release tagging signal may have changed: <describe the dependable signal found>.
> Suggested follow-up: update <the skill and/or workflow> to use <specific signal> instead of <current signal>.
```

Embed the machine-readable tracking block verbatim (so future runs can read prior
state). Fill every field from Step 1:

```yaml
# meai-otel-genai-worker:state:begin
upstream-repo: open-telemetry/semantic-conventions-genai
upstream-scan-ref: <scanned upstream commit SHA>
upstream-scan-date: <ISO-8601 UTC timestamp of this run>
upstream-release: <release version or "none">
core-semconv-dependency: <core semantic-conventions version>
dotnet-extensions-implemented-version: <gen-ai conventions version reflected in the code doc comments>
feedback-processed-through: <ISO-8601 UTC watermark: this run's start time when reviewer feedback was processed>
# meai-otel-genai-worker:state:end
```

On every incremental update, refresh `upstream-scan-ref` and `upstream-scan-date` to
the values from the current run. Set `feedback-processed-through` to the `run_started_at`
value from `/tmp/gh-aw/agent/target.json` (this run's start time, captured before the run
began) whenever a maintained draft PR exists -- see Step 4's "Honoring reviewer feedback";
on a **fresh** PR there is no prior feedback, so initialize it to the same `run_started_at`.
Carry the value forward unchanged only when there is no PR to maintain.

### Mandatory safe-output body preflight

Before emitting **any** `create-pull-request` or `update-pull-request` safe output, first
assemble its complete replacement `body` value, including the tracking block above. Do not
submit a summary, an abbreviated body, or a reference to a local file: the exact final body
string in the safe-output item must contain all of the following:

- one complete `# meai-otel-genai-worker:state:begin` ...
  `# meai-otel-genai-worker:state:end` block;
- a non-empty `upstream-scan-ref` set to `target.json.upstream_sha`; and
- a non-empty `feedback-processed-through` set to `target.json.run_started_at`.

Validate that final string before submitting the safe output (inspect the exact value you
will put in `body`, not an earlier draft). If any invariant is missing, rebuild the complete
body and validate it again; do **not** emit the `create-pull-request` or
`update-pull-request` item. This workflow never uses append or prepend body updates:
every `update-pull-request` body is a full replacement and must pass this preflight.

## Step 6 -- When the upstream release is published

If `target.json.release_ready` is `true` and a matching open PR has a pending release
action (the release gate in Step 3 routes here):
- Ensure the integration is complete and validated for the released version. The PR
  already lives on its existing branch -- do **not** create a new branch; keep
  pushing to it if final touch-ups are needed.
- Update the PR **title** so `{target}` resolves to the published `v{release}`
  (e.g. `...to v1.42.0`) and set `upstream-release` to that version in the body.
- If the PR is still draft, mark it **Ready for Review**
  (`mark-pull-request-as-ready-for-review`). If it is already ready, do not emit that
  safe output.
- Add a comment stating the upstream release is published and the integration should
  now be reviewed and merged.

## Safe outputs and no-op rules

- Use `create-pull-request` only on the fresh path.
- Use `push-to-pull-request-branch` only on the incremental path (behind open draft). It
  **appends** the run's new commit(s) to the PR branch -- the maintained branch is only ever
  added to, never force-pushed, amended, rebased, or reset, so its history stays a stable,
  growing trail of incremental updates.
- Use `update-pull-request` on the incremental (open draft), feedback-only, and release
  mark-ready paths to fully refresh (replace) an existing PR's description and title so the
  body always reflects the current integrated state; do not skip the body refresh because
  the code delta is small or empty. Do **not** use it on the behind non-draft **advisory**
  path -- that path only comments, leaving a PR a human has taken into review untouched.
- Use `add-comment` for incremental summaries, advisory notes on behind non-draft
  PRs, and the release-published note (Step 6). At most **one** comment per run.
- Use `mark-pull-request-as-ready-for-review` only when `target.json.release_ready` is
  `true` and the PR is still draft.
- When the matching PR is already caught up with the upstream SHA (or any run needs
  no visible change), do **not** post a no-op report, comment, issue, or PR. Emit the
  `noop` safe output, and **also** write a short explanation to the GitHub Actions
  **step summary** (see below). Do not create any repository-visible artifact.
- A no-op is valid in only two cases: (a) a matching PR's recorded `upstream-scan-ref`
  equals the scanned upstream SHA **and** no release action is pending for that PR, or
  (b) the completed Step 2 cross-reference
  finds zero merged upstream gen-ai convention changes ahead of the implemented version. Case (b)
  means **nothing upstream is unintegrated** -- it is **not** satisfied when upstream
  carries merged changes that you classified as deferred (new attributes without an
  emission site, uninstrumented capabilities, or doc-only clarifications). Those
  deferred items are a non-zero delta: they require an open/maintained draft PR that
  documents them, even though no constant is added for them yet. An unreleased upstream
  (`upstream-release: none`), an `Unreleased` CHANGELOG section, or the absence of a
  release tag are **never**, on their own, valid reasons to no-op.
- **No-op step summary:** whenever the run no-ops, append a concise Markdown
  explanation to the file at `$GITHUB_STEP_SUMMARY` (for example
  `echo "..." >> "$GITHUB_STEP_SUMMARY"`). This summary is attached to the workflow
  run only -- it is **not** a repository-visible report. Include: the target
  `{target}`, the scanned upstream SHA, the scan timestamp, which of the two no-op
  conditions was met, and -- when condition (a) applies -- the matched PR number/URL
  and its state (open draft / open / merged).
