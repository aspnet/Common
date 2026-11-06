---
name: "Agent Framework Template Worker"
description: >-
  Maintain a single draft pull request that bumps the aiagent-webapi project template's
  Microsoft.Agents.AI package versions in eng/packages/ProjectTemplates.props to the newest
  coherent Agent Framework release on the dotnet-public feed, CI-validated by restoring, building,
  and packing the template package through the repo's Arcade build and running its snapshot +
  execution tests. Invoked per-target by the Agent Framework Template Orchestrator (workflow_call)
  or manually (workflow_dispatch).

# Only run on a schedule for the canonical (non-fork) repository; allow manual dispatch anywhere.
if: ${{ github.event_name == 'workflow_dispatch' || !github.event.repository.fork }}

permissions:
  actions: read
  contents: read
  pull-requests: read
  issues: read

safe-outputs:
  # Threat detection screens the untrusted reviewer feedback this agent incorporates. It runs in a
  # separate gh-aw job that authenticates via the coalescing token below.
  threat-detection:
    engine:
      id: copilot
      env:
        # Workaround for github/gh-aw#43917: the detection job's `needs` omit `pat_pool`, so the
        # main engine's case(needs.pat_pool...) token can't resolve here. Authenticate by coalescing
        # the pool PAT secrets directly -- the first non-empty one wins.
        COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_PAT_0 || secrets.COPILOT_PAT_1 || secrets.COPILOT_PAT_2 || secrets.COPILOT_PAT_3 || secrets.COPILOT_PAT_4 || secrets.COPILOT_PAT_5 || secrets.COPILOT_PAT_6 || secrets.COPILOT_PAT_7 || secrets.COPILOT_PAT_8 || secrets.COPILOT_PAT_9 || secrets.COPILOT_GITHUB_TOKEN }}
  # The maintained PR is created, updated, and pushed to entirely through native gh-aw safe outputs
  # -- the agent job stays read-only and every write runs in a separate, permission-controlled job.
  # allowed-files restricts every write to the template's version file and template tree.
  create-pull-request:
    draft: true
    labels: [automation, area-ai-templates]
    base-branch: main
    preserve-branch-name: true
    if-no-changes: "warn"
    allowed-files:
      - "eng/packages/ProjectTemplates.props"
      - "src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/**"
      - "src/Libraries/Microsoft.Extensions.AI*/**"
      - "test/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.IntegrationTests/**"
  push-to-pull-request-branch:
    target: "*"
    required-labels: [automation, area-ai-templates]
    if-no-changes: "ignore"
    allowed-files:
      - "eng/packages/ProjectTemplates.props"
      - "src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/**"
      - "src/Libraries/Microsoft.Extensions.AI*/**"
      - "test/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.IntegrationTests/**"
  update-pull-request:
    target: "*"
    required-labels: [automation, area-ai-templates]
    title: true
    body: true
  add-comment:
    target: "*"
    required-labels: [automation, area-ai-templates]
    max: 1
  mark-pull-request-as-ready-for-review:
    target: "*"
    required-labels: [automation, area-ai-templates]
  noop:
    report-as-issue: false
  report-failure-as-issue: false

network:
  allowed:
    - defaults
    - dotnet

features:
  # Let a maintainer promote an external contributor's comment to "approved" by adding an
  # endorsement reaction (👍/❤️), so the agent then sees and can act on it. Requires the gh-proxy
  # mode (below) to attribute the reacting user.
  integrity-reactions: true

tools:
  # Route Safe Outputs through the CLI proxy instead of the native HTTP MCP endpoint on the
  # awmg-mcpg gateway, which the firewall denies (TCP_DENIED/403) before recommending the
  # non-compilable `awmgmcpg` network.allowed entry. Workaround for github/gh-aw#45915;
  # github.mode: gh-proxy (below) removes the native GitHub MCP endpoint for the same reason.
  cli-proxy: true
  github:
    mode: gh-proxy
    toolsets: [default]
    # Only content at "approved" integrity or higher reaches the agent. Write-access authors
    # (OWNER/MEMBER/COLLABORATOR) are approved automatically; feedback from anyone else is filtered
    # out unless a maintainer endorses it. This is the single source of truth for feedback trust.
    min-integrity: approved
  # Full bash: the agent applies the host-validated props file, runs git to position the PR branch,
  # and commits. The AWF sandbox + firewall, the read-only agent permissions, and the safe-outputs
  # allow-lists are the boundaries.
  bash: [":*"]

runs-on: ubuntu-latest
timeout-minutes: 180

# The agent commits the version bump and checks out the maintained PR branch for incremental
# appends. fetch: ["*"] pre-fetches every branch during the credentialed host checkout so the agent
# reaches the PR branch from an already-local ref.
checkout:
  fetch: ["*"]
  fetch-depth: 0

concurrency:
  group: agent-framework-worker
  cancel-in-progress: false

on:
  # Invoked per-target by the orchestrator as a reusable workflow so this runs in the orchestrator's
  # run context and inherits its actor, satisfying gh-aw activation's role check.
  workflow_call:
    inputs:
      target:
        description: "Framework release signal (JSON) from agent-framework-discover.cs."
        required: true
        type: string
  # Manual dispatch for standalone testing: provide a target JSON, or leave empty to have the setup
  # script run discovery itself.
  workflow_dispatch:
    inputs:
      target:
        description: "Framework release signal (JSON). Leave empty to resolve from the feed."
        required: false
        type: string
  permissions: {}

# Before the agent runs, deterministically prepare the run: resolve the target Agent Framework
# release, map it onto the template's package subset, CI-validate the prospective bump with a real
# restore + build, discover and classify the maintained draft PR, and compute the recommended
# lifecycle action. The agent consumes target.json instead of discovering or building anything.
steps:
  - name: Setup .NET
    uses: actions/setup-dotnet@9a946fdbd5fb07b82b2f5a4466058b876ab72bb2 # v5.3.0
    with:
      dotnet-version: "10.0.x"
  - name: Set up the run context and CI-validate the bump
    env:
      GH_TOKEN: ${{ github.token }}
      TARGET_JSON: ${{ inputs.target }}
    run: bash .github/scripts/agent-framework-worker-setup.sh

# After the agent runs, verify each full-body pull-request write still carries the tracking block the
# next run reads back to resume: the state block markers and a non-empty feedback-processed-through
# watermark. A body missing them would leave the next run unable to recover its place.
post-steps:
  - name: Validate agent output identity
    run: |
      set -euo pipefail
      out=/tmp/gh-aw/agent_output.json
      if [ ! -f "$out" ]; then
        echo "::notice::No agent output to validate"; exit 0
      fi
      idx=$(jq -r '(.items // []) | to_entries[]
        | select(.value.type=="create_pull_request"
            or (.value.type=="update_pull_request" and ((.value.operation // "replace")=="replace")))
        | select((.value.body // "") != "")
        | .key' "$out" 2>/dev/null || true)
      if [ -z "$idx" ]; then
        echo "::notice::No full-body PR-writing items -- nothing to validate"; exit 0
      fi
      # Validate exactly as agent-framework-worker-setup.sh reads the block next run: the begin/end
      # markers must be whole trimmed lines, and the recorded values are read from inside that block.
      # A looser substring match could pass here yet leave the body unparseable next run, wedging the
      # state machine -- so mirror that parser (whole-line markers + block extraction) and require the
      # recorded agent-framework-version and feedback-processed-through to be present within the block.
      marker="agent-framework-template"
      tracking_block() {
        awk -v b="# ${marker}:state:begin" -v e="# ${marker}:state:end" '
          { t=$0; sub(/\r$/,"",t); gsub(/^[[:space:]]+|[[:space:]]+$/,"",t) }
          t == b { inb=1; buf=$0 ORS; next }
          inb    { buf=buf $0 ORS; if (t == e) { inb=0; last=buf } }
          END    { if (inb) last=buf; printf "%s", last }'
      }
      tracking_value() {
        sed -n "s/^[[:space:]]*[-*+>]*[[:space:]]*$1:[[:space:]]*//p" |
          head -1 | tr -d '"'\''\r' | sed 's/[[:space:]]*#.*$//; s/[[:space:]]*$//'
      }
      rc=0
      while IFS= read -r i; do
        [ -n "$i" ] || continue
        typ=$(jq -r ".items[$i].type" "$out")
        body=$(jq -r ".items[$i].body" "$out")
        block=$(printf '%s' "$body" | tracking_block)
        miss=""
        if [ -z "$block" ]; then
          miss="$miss state-block(whole-line begin/end markers)"
        else
          [ -n "$(printf '%s' "$block" | tracking_value agent-framework-version)" ] || miss="$miss agent-framework-version"
          [ -n "$(printf '%s' "$block" | tracking_value feedback-processed-through)" ] || miss="$miss feedback-processed-through"
        fi
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

# Agent Framework Template Worker

## Goal

Keep the `aiagent-webapi` project template aligned with the newest coherent Microsoft Agent
Framework release by continuously maintaining a **single draft pull request** against `main` that
bumps the Agent Framework package versions in `eng/packages/ProjectTemplates.props`. Each run
consumes the host-prepared `target.json`, positions and updates that one PR, and leaves it in a
correct, idempotent state. The automation **never merges** -- humans review and merge.

The repository skill `update-agent-framework-template` (in `.github/skills/`) is the authority for
**how** to map the release signal onto the template's packages and format the PR. This workflow
governs **lifecycle/idempotency** (which PR to touch and what state to leave it in).

## Step 0 -- Read the run context (authoritative)

Read `/tmp/gh-aw/agent/target.json` (written by the setup script -- do **not** re-discover or
re-build). Key fields:

- `release_version`, `release_date`, `source_feed` -- the target Agent Framework release.
- `desired_versions` -- the exact `{ packageId: version }` the template must pin (already mapped to
  the template's subset; OpenAI tracks the core version).
- `current_version`, `main_needs_bump` -- what `main` pins today and whether it differs.
- `validated`, `build_summary` -- whether the prospective bump built through the repo's Arcade build:
  restore + build + **pack** of the `Microsoft.Agents.AI.ProjectTemplates` package (the `.nupkg` the
  execution tests install). The exact validated props content is at
  `/tmp/gh-aw/agent/ProjectTemplates.props.bumped` and the bumped template package project at
  `/tmp/gh-aw/agent/ProjectTemplates.csproj.bumped`.
- `pr`, `pr_state`, `pr_is_draft`, `pr_branch` -- the maintained PR (if any).
- `pr_recorded_version` -- the release the maintained PR already bumped to (from its tracking block).
- `classification` -- `ours` / `adopt` / `blocked` / `none` (see below).
- `has_new_feedback`, `watermark`, `run_started_at` -- review-activity wake gate and watermarks.
- `action` -- the recommended lifecycle action (`produce` or `noop`).
- `desired_branch` (`update-agent-framework-template`), `base_branch` (`main`), `props_path`.

Classifications:
- **`ours`** -- an open `automation`+`area-ai-templates` PR on `pr_branch` carrying the
  `agent-framework-template:state` block. Maintain it.
- **`adopt`** -- an open `automation`+`area-ai-templates` **draft** PR on `desired_branch` with **no**
  tracking block yet (a human bootstrapped it). It passes both the label and draft gates: take it
  over and write the full tracking block this run.
- **`blocked`** -- a PR occupies `desired_branch` but fails a takeover gate: it is missing the
  automation labels, or it is a labeled non-draft (ready) PR a human is finalizing.
  **Emit `noop` and stand down** -- a human owns it.
- **`none`** -- no open maintained PR.

## Step 1 -- Guard rails

- If `action` is `noop` **or** `classification` is `blocked`: emit a `noop` safe output. Write the
  reason to the step summary. Do nothing else.
- If `validated` is `false`: the prospective bump did **not** build. **Never** open or update a PR
  with unvalidated versions. If a maintained PR exists (`ours`/`adopt`), post a single `add-comment`
  noting the build failure (quote the `build_summary`) and stop; otherwise emit `noop`. Do not emit
  `create-pull-request`, `push-to-pull-request-branch`, or `update-pull-request`.

## Step 2 -- Choose the action

With `validated: true`, pick the path from `classification` and the PR state:

| classification | PR state | Action |
|---|---|---|
| `none` | -- | **Fresh PR** (Step 3a) |
| `ours` / `adopt` | behind (`pr_recorded_version` != `release_version`), **draft** | **Incremental update** (Step 3b) |
| `ours` / `adopt` | caught up (`pr_recorded_version` == `release_version`), `has_new_feedback` = true | **Feedback update** (Step 3c) |
| `ours` / `adopt` | caught up, `has_new_feedback` = false | **No-op** (already handled by `action: noop`) |
| `ours` | behind, **non-draft** | **Advisory only** (Step 3d) |

For `adopt`, additionally write the **full tracking block** into the body this run so the PR becomes
`ours`.

## Step 3 -- Apply and publish

Apply the changes by copying the host-prepared files into place -- never hand-edit versions, so what
you publish is exactly what was CI-validated:

```bash
# The Agent Framework package versions (lockstep, all Microsoft.Agents.AI packages):
cp /tmp/gh-aw/agent/ProjectTemplates.props.bumped eng/packages/ProjectTemplates.props
# The template NuGet package's own version, aligned to the release (Major/Minor/Patch; label kept):
cp /tmp/gh-aw/agent/ProjectTemplates.csproj.bumped "$(jq -r '.template_pkg_proj' /tmp/gh-aw/agent/target.json)"
```

Stage both files together in every commit. `target.json.template_pkg_old` -> `template_pkg_new` is the
template package version change (e.g. `1.3.0-preview` -> `1.13.0-preview`); the prerelease label is
never changed by the automation.

### 3a. Fresh PR (`classification: none`)

The run is checked out on `main` (the base). To avoid advancing `main` or producing an empty patch,
**detach HEAD first**, then apply, commit, and create a branch ref without switching:

```bash
git checkout --detach
cp /tmp/gh-aw/agent/ProjectTemplates.props.bumped eng/packages/ProjectTemplates.props
cp /tmp/gh-aw/agent/ProjectTemplates.csproj.bumped "$(jq -r '.template_pkg_proj' /tmp/gh-aw/agent/target.json)"
git add eng/packages/ProjectTemplates.props "$(jq -r '.template_pkg_proj' /tmp/gh-aw/agent/target.json)"
git commit -m "Update Agent Framework to <release_version>"
```

Choose the branch name: use `desired_branch`. Check the remote first
(`git ls-remote --heads origin "$DESIRED_BRANCH"`); if it already exists, suffix with the run id
(`{desired_branch}_{GITHUB_RUN_ID}`) and note the deviation in the body. Create the ref **without
switching** (`git branch {branch} HEAD`), then emit **`create-pull-request`** with:
- branch `{branch}`, title `Update Agent Framework to <release_version>`,
- the body from Step 4 (including the tracking block), draft (configured by the safe output).

### 3b. Incremental update (behind, draft, `ours`/`adopt`)

The PR branch already exists on the remote. Fetch and check it out, apply, and **append** a commit
(never amend/rebase/reset/squash/force-push):

```bash
git fetch origin "$PR_BRANCH" && git checkout "$PR_BRANCH"
cp /tmp/gh-aw/agent/ProjectTemplates.props.bumped eng/packages/ProjectTemplates.props
cp /tmp/gh-aw/agent/ProjectTemplates.csproj.bumped "$(jq -r '.template_pkg_proj' /tmp/gh-aw/agent/target.json)"
git add eng/packages/ProjectTemplates.props "$(jq -r '.template_pkg_proj' /tmp/gh-aw/agent/target.json)"
git commit -m "Update Agent Framework to <release_version>"
```

Emit **`push-to-pull-request-branch`** (target the PR), **`update-pull-request`** (full-body replace
per Step 4; for `adopt`, this adds the tracking block), and one **`add-comment`** summarizing the
delta (old -> new versions).

### 3c. Feedback update (caught up, `has_new_feedback`)

Address the approved reviewer feedback visible on the PR (the framework's `min-integrity: approved`
filtering already screens who is trusted). Only act on feedback **created after `watermark`**, and
only within the allowed files (`eng/packages/ProjectTemplates.props`,
`src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/**`). Reject out-of-scope requests with a
brief explanation. If the feedback yields a file change, checkout `pr_branch`, apply it, and
`push-to-pull-request-branch`. Always **refresh the body** (`update-pull-request`, advancing
`feedback-processed-through` to `run_started_at`) and post one **`add-comment`** summarizing what you
did. If nothing was actionable, still refresh the body so the watermark advances.

### 3d. Advisory only (behind, non-draft, `ours`)

Do **not** implement. Post one **`add-comment`** noting that a newer Agent Framework release
(`release_version`) is available and that re-marking the PR as draft lets the next scheduled run
apply it. Do not update the body.

### 3e. Evaluate Agent Framework changes and keep consumption current (fresh / incremental paths)

Bumping versions is not the whole job: the Agent Framework surface can change between releases, so on
the fresh and incremental paths you must also confirm the template and any **other** Agent Framework
consumption in the repo still work and follow the currently prescribed patterns.

1. **Read the changes.** The host wrote the Agent Framework release notes spanning
   `target.json.from_version` -> `release_version` to
   `/tmp/gh-aw/agent/<target.json.af_changes_path>` (`target.json.af_change_count` releases). Read it
   and identify API changes, removals/deprecations, renames, and newly recommended patterns.
2. **Find the consumption.** The template lives under
   `src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/**`; other consumption is under
   `src/Libraries/Microsoft.Extensions.AI*/**`. Use the skill's
   guidance to locate every place these consume Agent Framework types/APIs.
3. **Apply required updates.** For any consumption affected by the changes, update it to the current
   prescribed pattern -- but only within the allowed files (the template tree, the
   `Microsoft.Extensions.AI*` libraries, `eng/packages/ProjectTemplates.props`, and the template
   integration-test tree). Commit these alongside the version bump. Reject or skip anything outside
   that scope and note it in the PR body.
4. **Ensure the tests pass.** The host already ran the template's snapshot + execution tests
   against the bump (`target.json.tests_summary`). If you changed template **content** (not just
   versions), the snapshot tests will no longer match -- regenerate the affected `.verified`
   snapshots under
   `test/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.IntegrationTests/Snapshots/**` so they
   reflect the intended new content, and re-run the tests. Never publish with failing tests: if you
   cannot make them pass within the allowed files, emit `report_incomplete` describing why.
5. If your evaluation finds **no** required consumption changes, that is a valid outcome -- record in
   the PR body that the changes were reviewed and no consumption updates were needed.

## Step 4 -- PR body and tracking block

Regenerate the **entire** body on every full-body write. Include, succinctly:

1. A one-line summary: bumping the Microsoft.Agents.AI packages to Agent Framework `release_version`.
2. A table of the package version changes (old -> new) for **every** entry in `desired_versions`
   (all Microsoft.Agents.AI packages move together; note each package keeps its own tier, so some
   rows are `-preview.*` or `-alpha.*`).
3. The **template package version** change: `template_pkg_old` -> `template_pkg_new` (the
   `Microsoft.Agents.AI.ProjectTemplates` package's own version, aligned to the release
   Major/Minor/Patch with its prerelease label unchanged).
4. **CI validation**: state that `eng/packages/ProjectTemplates.props` and the template package
   version were bumped and the `Microsoft.Agents.AI.ProjectTemplates` package restored, built, and
   packed successfully through the repo's Arcade build (quote `build_summary`), and that the
   snapshot + execution tests passed (quote `tests_summary`).
5. **Agent Framework changes reviewed**: note the `af_change_count` releases evaluated across
   `from_version` -> `release_version`, and either the consumption updates you made or that no
   consumption changes were required.
6. A note that the automation maintains this draft; a human reviews and merges.

End the body with the machine-readable tracking block as the very last thing, wrapped in a `yaml`
code fence. The fence lines are **required** -- without them the `#` marker lines render as Markdown
headings instead of code. Reproduce the fence and the block exactly as shown (opening ```` ```yaml ````,
the block, closing ```` ``` ````), substituting the values:

```yaml
# agent-framework-template:state:begin
source-feed: <source_feed>
agent-framework-version: <release_version>
agent-framework-release-date: <release_date>
feedback-processed-through: <run_started_at>
# agent-framework-template:state:end
```

The post-run identity check fails the run if a full-body PR write is missing these markers or the
`feedback-processed-through` watermark, so always include them.

### Mandatory safe-output body preflight

Before emitting **any** `create-pull-request` or `update-pull-request` safe output, first assemble
its complete replacement `body` value, including the tracking block above. Do not submit a summary,
an abbreviated body, or a reference to a local file: the exact final body string in the safe-output
item must contain all of the following:

- one complete `# agent-framework-template:state:begin` ...
  `# agent-framework-template:state:end` block;
- a non-empty `agent-framework-version` set to `target.json.release_version`; and
- a non-empty `feedback-processed-through` set to `target.json.run_started_at`.

Validate that final string before submitting the safe output (inspect the exact value you will put
in `body`, not an earlier draft). If any invariant is missing, rebuild the complete body and validate
it again; do **not** emit the `create-pull-request` or `update-pull-request` item. This workflow never
uses append or prepend body updates: every `update-pull-request` body is a full replacement and must
pass this preflight.

## Notes

- The automation keeps the PR a **draft**; `mark-pull-request-as-ready-for-review` is reserved for a
  future code-complete signal and is not emitted in the normal flow.
- Never touch files outside the allowed list. Never edit `.github/**`, `global.json`, `nuget.config`,
  `Directory.Packages.props`, or any solution file. If a build produced `bin/`/`obj/`, do not stage
  them.
- Settle contradictory feedback by recency (latest timestamp wins).
