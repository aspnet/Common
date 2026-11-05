#!/usr/bin/env bash
# Host-side setup for the "MEAI: Otel GenAI Worker" agentic workflow.
#
# Deterministically prepares everything the agent needs before it runs so the agent
# never has to discover, filter, or de-duplicate state itself:
#   1. Resolves the upstream scan target (from the orchestrator's `target` input, or from a
#      standalone dispatch's upstream_ref / default HEAD), retrying transient failures and
#      hard-failing when the ref cannot be resolved to a commit SHA at all.
#   2. Discovers the maintained draft PR and classifies it: OURS (carries our tracking
#      marker), ADOPT (a human-bootstrapped automation+area-ai PR on our branch with no
#      marker yet), BLOCKED (a non-automation PR occupying our branch -- a human owns it),
#      or NONE.
#   3. Reads the PR's recorded upstream-scan-ref, upstream-release, and
#      feedback-processed-through watermark, and computes a recommended lifecycle action.
#   4. Detects whether any PR review activity is newer than the recorded watermark. This
#      is an author-agnostic wake gate only -- it never reads comment bodies. The agent
#      collects and evaluates the actual review feedback itself under gh-aw integrity
#      filtering (min-integrity + integrity-reactions), the single source of truth for
#      which feedback is trusted and actionable.
#
# Writes (under $AGENT_DIR, uploaded in the agent artifact so downstream jobs read them):
#   target.json  resolved scan target + PR discovery + recommended action, plus the
#                feedback-processed-through watermark and this run's run_started_at (which
#                the agent stamps back as the new watermark)
#
# The recommended action lets a caught-up run early-noop before the expensive build. The
# agent's Step 3 remains authoritative and refines edge cases (e.g. release-gate mark-ready).
#
# A transient API blip must never masquerade as "no PR / no work"; discovery is retried
# and, when the maintained PR cannot be established with confidence, the action defaults
# to "produce" (never a silent skip).
#
# Environment:
#   GITHUB_REPOSITORY  owner/repo (set by Actions)
#   GH_TOKEN           token with pull-requests:read (the agent job's github.token)
#   TARGET_JSON        target object from the orchestrator (workflow_call); may be empty
#   UPSTREAM_REF       standalone-dispatch scan ref override (used only when TARGET_JSON empty)
#   AGENT_DIR          output dir (default /tmp/gh-aw/agent)
set -euo pipefail

AGENT_DIR="${AGENT_DIR:-/tmp/gh-aw/agent}"
REPO="${GITHUB_REPOSITORY:-}"
TARGET_JSON="${TARGET_JSON:-}"
UPSTREAM_REF="${UPSTREAM_REF:-}"
mkdir -p "$AGENT_DIR"
target_file="$AGENT_DIR/target.json"

run_started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# ---- 1. Resolve the upstream scan target -------------------------------------------
UPSTREAM_REPO="open-telemetry/semantic-conventions-genai"
DESIRED_BRANCH="update-otel-genai-to-latest"
# The maintained PR always targets main (see the worker's create-pull-request
# `base-branch: main` safe output). PR discovery restricts to this base so a tracking-branch
# PR opened against some other base is never mistaken for the maintained PR.
BASE_BRANCH="main"
# The state block's delimiters are hard-coded to this workflow's name -- a worker-internal
# concern the orchestrator neither knows nor supplies. The block is delimited by whole yaml-comment
# lines `# ${STATE_MARKER}:state:begin` / `:state:end`.
STATE_MARKER="meai-otel-genai-worker"
upstream_sha=""
if [ -n "$TARGET_JSON" ]; then
	UPSTREAM_REPO="$(jq -r '.upstream_repo // "open-telemetry/semantic-conventions-genai"' <<<"$TARGET_JSON")"
	DESIRED_BRANCH="$(jq -r '.desired_branch // "update-otel-genai-to-latest"' <<<"$TARGET_JSON")"
	UPSTREAM_REF="$(jq -r '.upstream_ref // ""' <<<"$TARGET_JSON")"
	upstream_sha="$(jq -r '.upstream_sha // ""' <<<"$TARGET_JSON")"
fi

resolve_sha() {
	local ref="$1" sha="" attempt db
	for attempt in 1 2 3; do
		if [ -z "$ref" ]; then
			db="$(gh api "repos/${UPSTREAM_REPO}" -q '.default_branch' 2>/dev/null || true)"
			[ -n "$db" ] && sha="$(gh api "repos/${UPSTREAM_REPO}/commits/${db}" -q '.sha' 2>/dev/null || true)"
		else
			sha="$(gh api "repos/${UPSTREAM_REPO}/commits/${ref}" -q '.sha' 2>/dev/null || true)"
		fi
		[ -n "$sha" ] && { printf '%s' "$sha"; return 0; }
		[ "$attempt" -lt 3 ] && sleep 2
	done
	# All retries exhausted without resolving a SHA. Signal failure with a non-zero return;
	# the caller runs this in a command substitution guarded with `|| true` and then hard-
	# errors on the empty result, so `set -e` never pre-empts that explicit diagnostic.
	return 1
}

resolve_release_commit() {
	local tag="$1" ref_json="" object_type="" object_sha="" tag_json=""
	[ -z "$tag" ] && return 0

	ref_json="$(gh api "repos/${UPSTREAM_REPO}/git/ref/tags/${tag}" 2>/dev/null || true)"
	object_type="$(jq -r '.object.type // empty' <<<"$ref_json" 2>/dev/null || true)"
	object_sha="$(jq -r '.object.sha // empty' <<<"$ref_json" 2>/dev/null || true)"
	case "$object_type" in
		commit)
			[ -n "$object_sha" ] && { printf '%s' "$object_sha"; return 0; } ;;
		tag)
			tag_json="$(gh api "repos/${UPSTREAM_REPO}/git/tags/${object_sha}" 2>/dev/null || true)"
			object_type="$(jq -r '.object.type // empty' <<<"$tag_json" 2>/dev/null || true)"
			object_sha="$(jq -r '.object.sha // empty' <<<"$tag_json" 2>/dev/null || true)"
			[ "$object_type" = "commit" ] && [ -n "$object_sha" ] && { printf '%s' "$object_sha"; return 0; } ;;
	esac

	# Fallback for lightweight tags or unusual ref layouts; the commits endpoint
	# resolves branch/tag names to the commit GitHub would show for that ref.
	object_sha="$(gh api "repos/${UPSTREAM_REPO}/commits/${tag}" -q '.sha' 2>/dev/null || true)"
	[ -n "$object_sha" ] && printf '%s' "$object_sha"
	# Always succeed: the result is communicated via stdout (the caller tests for a
	# non-empty commit), so an unresolved tag must degrade to low confidence -- never
	# abort the run under `set -e` when this test is the function's final command.
	return 0
}

if [ -z "$upstream_sha" ] && [ -n "${GH_TOKEN:-}" ]; then
	upstream_sha="$(resolve_sha "$UPSTREAM_REF")" || true
	if [ -z "$upstream_sha" ]; then
		echo "::error::Could not resolve upstream ref '${UPSTREAM_REF:-<default branch HEAD>}' in ${UPSTREAM_REPO} to a commit SHA after retries. Refusing to continue with an unresolved scan target."
		exit 1
	fi
fi

# Best-effort latest published gen-ai release tag (normally none -- Development stability).
upstream_release="none"
upstream_release_commit=""
release_matches_scan="false"
release_matches_pr="false"
release_ready="false"
release_detection_confidence="none"
release_detection_signal="No GitHub release is published for the upstream repo."
if [ -n "${GH_TOKEN:-}" ]; then
	rel="$(gh api "repos/${UPSTREAM_REPO}/releases/latest" -q '.tag_name' 2>/dev/null || true)"
	if [ -n "$rel" ]; then
		upstream_release="$rel"
		upstream_release_commit="$(resolve_release_commit "$rel")"
		if [ -n "$upstream_release_commit" ]; then
			release_detection_confidence="high"
			release_detection_signal="GitHub latest release ${rel} resolves through refs/tags to commit ${upstream_release_commit}."
			[ -n "$upstream_sha" ] && [ "$upstream_release_commit" = "$upstream_sha" ] && release_matches_scan="true"
		else
			release_detection_confidence="low"
			release_detection_signal="GitHub latest release ${rel} exists, but its tag commit could not be resolved; release-gate readiness is intentionally withheld."
		fi
	fi
fi

write_target() {
	# $1=pr $2=pr_state $3=pr_is_draft $4=pr_recorded_sha $5=pr_recorded_release
	# $6=classification(ours|adopt|blocked|none) $7=action
	jq -cn \
		--arg upstream_repo "$UPSTREAM_REPO" --arg upstream_ref "$UPSTREAM_REF" \
		--arg upstream_sha "$upstream_sha" --arg upstream_release "$upstream_release" \
		--arg upstream_release_commit "$upstream_release_commit" \
		--argjson release_matches_scan "$release_matches_scan" \
		--argjson release_matches_pr "$release_matches_pr" \
		--argjson release_ready "$release_ready" \
		--arg release_detection_confidence "$release_detection_confidence" \
		--arg release_detection_signal "$release_detection_signal" \
		--arg desired_branch "$DESIRED_BRANCH" --arg pr_branch "${PR_BRANCH:-}" \
		--arg pr "$1" --arg pr_state "$2" --argjson pr_is_draft "${3:-false}" \
		--arg pr_recorded_sha "$4" --arg pr_recorded_release "$5" \
		--arg classification "$6" --arg action "$7" \
		--argjson has_new_feedback "${has_new_feedback:-false}" \
		--arg watermark "${watermark:-}" --arg run_started_at "$run_started_at" \
		'{upstream_repo:$upstream_repo, upstream_ref:$upstream_ref, upstream_sha:$upstream_sha,
		  upstream_release:$upstream_release, upstream_release_commit:$upstream_release_commit,
		  release_matches_scan:$release_matches_scan, release_matches_pr:$release_matches_pr,
		  release_ready:$release_ready,
		  release_detection_confidence:$release_detection_confidence,
		  release_detection_signal:$release_detection_signal,
		  desired_branch:$desired_branch, pr_branch:$pr_branch,
		  pr:$pr, pr_state:$pr_state, pr_is_draft:$pr_is_draft, pr_recorded_sha:$pr_recorded_sha,
		  pr_recorded_release:$pr_recorded_release, classification:$classification, action:$action,
		  has_new_feedback:$has_new_feedback,
		  watermark:$watermark, run_started_at:$run_started_at}' >"$target_file"
}

step_summary() {
	# $1=classification $2=action $3=pr $4=pr_recorded_sha $5=new_feedback
	{
		echo "## Otel GenAI worker -- setup decision"
		echo ""
		echo "| field | value |"
		echo "|---|---|"
		echo "| upstream_repo | \`${UPSTREAM_REPO}\` |"
		echo "| scan ref | \`${UPSTREAM_REF:-<default HEAD>}\` |"
		echo "| upstream SHA | \`${upstream_sha:-<unresolved>}\` |"
		echo "| upstream release | \`${upstream_release}\` |"
		echo "| upstream release commit | \`${upstream_release_commit:-<unresolved>}\` |"
		echo "| release detection confidence | \`${release_detection_confidence}\` |"
		echo "| release matches scanned SHA | ${release_matches_scan} |"
		echo "| release matches PR SHA | ${release_matches_pr} |"
		echo "| release ready | ${release_ready} |"
		echo "| maintained PR | ${3:-<none>} |"
		echo "| maintained PR branch | \`${PR_BRANCH:-<none>}\` |"
		echo "| PR recorded SHA | \`${4:-<none>}\` |"
		echo "| classification | **${1}** |"
		echo "| recommended action | **${2}** |"
		echo "| new review activity | ${5} |"
		[ "${feedback_query_failed:-false}" = "true" ] && echo "| review-activity query | **failed** -- wake gate opened, run will produce |"
		[ "${tracking_valid_count:-0}" -gt 1 ] && echo "| valid tracking PR candidates | ${tracking_valid_count} (newest selected) |"
	} >>"${GITHUB_STEP_SUMMARY:-/dev/null}" 2>/dev/null || true
}

fetch_pr_body() {
	local pr_number="$1" battempt
	body="" body_ok="false"
	for battempt in 1 2 3; do
		if body="$(gh pr view "$pr_number" --repo "$REPO" --json body -q '.body' 2>/dev/null)"; then
			body_ok="true"; return 0
		fi
		[ "$battempt" -lt 3 ] && sleep 2
	done
	return 0
}

tracking_value() {
	local name="$1"
	# Tolerate an optional leading markdown list/quote marker (-, *, +, >) before the field
	# name. The identity guard that gates a PR write accepts the field in this same anchored,
	# tolerant form, so anything it admits is recoverable here.
	sed -n "s/^[[:space:]]*[-*+>]*[[:space:]]*${name}:[[:space:]]*//p" |
		head -1 | tr -d '"'\''\r' | sed 's/[[:space:]]*#.*$//; s/[[:space:]]*$//'
}

tracking_block() {
	# Emit only the machine-managed state block -- the fenced yaml block the agent writes as
	# the VERY LAST thing in the body, delimited by the visible `# ${STATE_MARKER}:state:begin`
	# / `:state:end` comment lines (matched as whole comment lines: leading indent and a
	# trailing CR are tolerated, but the "# " prefix and the ":state:begin"/":state:end" suffix
	# must be exact). Take the LAST begin..end range so human prose anywhere above it -- a quoted
	# or bulleted "field:" line, or an older state block a maintainer pastes in as a note --
	# cannot win tracking_value's first match and force a false caught-up no-op or silently
	# suppress real reviewer feedback via a bogus watermark. A begin with no matching end (a
	# human truncated the block) still yields its fields rather than an empty read.
	awk -v b="# ${STATE_MARKER}:state:begin" -v e="# ${STATE_MARKER}:state:end" '
		{ t=$0; sub(/\r$/,"",t); gsub(/^[[:space:]]+|[[:space:]]+$/,"",t) }
		t == b { inb=1; buf=$0 ORS; next }
		inb    { buf=buf $0 ORS; if (t == e) { inb=0; last=buf } }
		END    { if (inb) last=buf; printf "%s", last }'
}

body_has_state_marker() {
	# true iff $1 carries the state block's begin delimiter as a whole yaml-comment line.
	# Feed the body on stdin (not a jq --arg) so an oversized PR body can never hit an argv
	# length limit, and trim each line's ends with \s -- the same whitespace set awk's
	# [[:space:]] trims in tracking_block and the publish guardrail -- so ownership detection
	# and block extraction can never disagree. Leading indent and a trailing CR are tolerated;
	# the "# " prefix and ":state:begin" suffix must be exact.
	printf '%s' "$1" | jq -Rrs --arg m "# ${STATE_MARKER}:state:begin" \
		'split("\n") | any(gsub("^\\s+|\\s+$";"") == $m)'
}

watermark=""
has_new_feedback="false"

if [ -z "$REPO" ] || [ -z "${GH_TOKEN:-}" ]; then
	echo "GITHUB_REPOSITORY or GH_TOKEN unset; cannot discover PR -- defaulting to produce (fresh)"
	write_target "" "" false "" "" "none" "produce"
	step_summary "none" "produce" "" "" "false"
	exit 0
fi

# ---- 2. Discover the maintained PR and classify it ---------------------------------
# Stage 1: query the canonical branch server-side by exact head. This avoids the
# default `gh pr list` first-30 limit and preserves the invariant that a human-owned PR
# on the canonical branch blocks the automation.
BASE_OWNER="${REPO%%/*}"
pr="" PR_BRANCH="" pr_labels="" pr_is_draft="false" classification="none" body_ok="false" tracking_valid_count=0
for attempt in 1 2 3; do
	rows="$(gh api --method GET "repos/${REPO}/pulls" \
		-f state=open -f head="${BASE_OWNER}:${DESIRED_BRANCH}" -f base="${BASE_BRANCH}" -f per_page=100 2>/dev/null \
		| jq -c --arg owner "$BASE_OWNER" --arg branch "$DESIRED_BRANCH" --arg base "$BASE_BRANCH" \
			'[.[] | select((.head.repo.owner.login // "") == $owner and .head.ref == $branch and (.base.ref // "") == $base)
			  | {number, headRefName:.head.ref, headRepositoryOwner:{login:(.head.repo.owner.login // "")},
			     isDraft:(.draft // false), labels:(.labels // []), updatedAt:.updated_at}]
			 | sort_by(.updatedAt) | reverse' 2>/dev/null || true)"
	[ -n "$rows" ] && break
	[ "$attempt" -lt 3 ] && sleep 2
done
rows="${rows:-[]}"

pr="$(jq -r '(.[0].number) // empty' <<<"$rows" 2>/dev/null || true)"
if [ -n "$pr" ]; then
	pr_is_draft="$(jq -r '.[0].isDraft // false' <<<"$rows")"
	PR_BRANCH="$(jq -r '.[0].headRefName // ""' <<<"$rows")"
	has_automation="$(jq -r '[.[0].labels[]?.name] | (index("automation") != null) and (index("area-ai") != null)' <<<"$rows")"
	# Fetch the PR body with retries. A transient blip must not drop our tracking
	# marker -- that would misclassify our own PR as a fresh adopt and replay feedback
	# off a phantom watermark; body_ok tells a real empty body from a failed fetch.
	fetch_pr_body "$pr"
	has_marker="$(body_has_state_marker "$body")"
	if [ "$body_ok" != "true" ] && [ "$has_automation" = "true" ]; then
		# Body unreadable after retries but our automation labels are present: almost
		# certainly our own PR mid-blip. Treat as ours -- the agent re-reads the real
		# body and reconciles -- rather than re-adopting or replaying stale feedback.
		classification="ours"
	elif [ "$has_automation" = "true" ] && [ "$has_marker" = "true" ]; then
		classification="ours"
	elif [ "$has_automation" = "true" ] && [ "$has_marker" != "true" ]; then
		# Automation-labeled PR on our branch with no tracking marker yet: a human
		# bootstrapped it -- adopt it (write the marker on this run's update).
		classification="adopt"
	else
		# A PR occupies our branch but is not our automation PR: a human owns it. Stand down.
		classification="blocked"
	fi
else
	# Stage 2: no canonical PR exists. Look for a prior automation tracking PR on the
	# canonical branch or on a run-suffixed branch (`desired_branch_<run_id>`). Keep this
	# lightweight and fetch each candidate body individually so open PR history cannot
	# grow into a truncated bulk-body parse.
	for attempt in 1 2 3; do
		rows="$(gh pr list --repo "$REPO" --state open --base "$BASE_BRANCH" --label automation --label area-ai --limit 1000 \
			--json number,headRefName,headRepositoryOwner,isDraft,labels,updatedAt,baseRefName 2>/dev/null \
			| jq -c --arg owner "$BASE_OWNER" --arg branch "$DESIRED_BRANCH" --arg base "$BASE_BRANCH" \
				'map(select((.headRepositoryOwner.login // "") == $owner
				  and (.baseRefName // "") == $base
				  and (.headRefName == $branch
				    or ((.headRefName | startswith($branch + "_"))
				      and ((.headRefName | ltrimstr($branch + "_")) | test("^[0-9]+$"))))))
				 | sort_by(.updatedAt) | reverse' 2>/dev/null || true)"
		[ -n "$rows" ] && break
		[ "$attempt" -lt 3 ] && sleep 2
	done
	rows="${rows:-[]}"
	row_count="$(jq -r 'length' <<<"$rows" 2>/dev/null || echo 0)"
	selected_body=""
	for i in $(seq 0 $((row_count - 1)) 2>/dev/null); do
		candidate_pr="$(jq -r ".[$i].number // empty" <<<"$rows")"
		[ -n "$candidate_pr" ] || continue
		fetch_pr_body "$candidate_pr"
		[ "$body_ok" = "true" ] || continue
		[ "$(body_has_state_marker "$body")" = "true" ] || continue
		candidate_sha="$(printf '%s\n' "$body" | tracking_block | tracking_value "upstream-scan-ref")"
		printf '%s' "$candidate_sha" | grep -Eq '^[0-9a-fA-F]{7,}$' || continue
		tracking_valid_count=$((tracking_valid_count + 1))
		if [ -z "$pr" ]; then
			pr="$candidate_pr"
			pr_is_draft="$(jq -r ".[$i].isDraft // false" <<<"$rows")"
			PR_BRANCH="$(jq -r ".[$i].headRefName // \"\"" <<<"$rows")"
			classification="ours"
			selected_body="$body"
		fi
	done
	if [ -n "$pr" ]; then
		body="$selected_body"
		body_ok="true"
	fi
fi

# ---- 3. Read recorded state + compute the recommended action -----------------------
pr_recorded_sha="" pr_recorded_release=""
if [ -n "$pr" ] && [ "$classification" != "blocked" ] && [ "$body_ok" = "true" ]; then
	watermark="$(printf '%s\n' "$body" \
		| tracking_block | tracking_value "feedback-processed-through")"
	pr_recorded_sha="$(printf '%s\n' "$body" \
		| tracking_block | tracking_value "upstream-scan-ref")"
	pr_recorded_release="$(printf '%s\n' "$body" \
		| tracking_block | tracking_value "upstream-release")"
fi
[ -n "$upstream_release_commit" ] && [ -n "$pr_recorded_sha" ] && [ "$upstream_release_commit" = "$pr_recorded_sha" ] && release_matches_pr="true"
[ "$release_matches_pr" = "true" ] && release_ready="true"

# ---- 4. Wake gate: is there any review activity newer than the watermark? -----------
# Author-agnostic and body-free: this only decides whether a caught-up PR is worth waking
# the agent for. It does NOT read, trust, or filter comment content -- the agent collects
# the actual review feedback itself under gh-aw integrity filtering. Over-waking (e.g. for
# an un-endorsed external comment the agent will not act on) is harmless; under-waking is
# not: each query is retried, and if any still fails the real count is unknown, so the gate
# opens (has_new_feedback=true) and produces, letting the agent's own Step 3 analysis and
# independent feedback discovery stay authoritative instead of trusting a false no-op.
feedback_query_failed="false"
# Retry an activity query up to 3 times, appending its timestamps to $feedback_times only on
# a clean fetch. The discovery and PR-body reads above already retry; matching that here keeps
# a single transient API blip from opening the gate and forcing a needless produce.
fetch_activity() {
	local endpoint="$1" jqexpr="$2" fattempt scratch
	scratch="$(mktemp)"
	for fattempt in 1 2 3; do
		if gh api "$endpoint" --paginate -q "$jqexpr" >"$scratch" 2>/dev/null; then
			cat "$scratch" >>"$feedback_times"; rm -f "$scratch"; return 0
		fi
		[ "$fattempt" -lt 3 ] && sleep 2
	done
	rm -f "$scratch"; return 1
}
if [ -n "$pr" ] && [ "$classification" != "blocked" ]; then
	feedback_times="$(mktemp)"
	fetch_activity "repos/${REPO}/issues/${pr}/comments" '.[] | select(.user.type != "Bot") | .created_at'   || feedback_query_failed="true"
	fetch_activity "repos/${REPO}/pulls/${pr}/comments"  '.[] | select(.user.type != "Bot") | .created_at'   || feedback_query_failed="true"
	fetch_activity "repos/${REPO}/pulls/${pr}/reviews"   '.[] | select(.user.type != "Bot") | .submitted_at' || feedback_query_failed="true"
	if [ "$feedback_query_failed" = "true" ]; then
		echo "::warning::review-activity query failed for PR #${pr} after retries; opening the wake gate so this run produces instead of risking a false no-op"
		has_new_feedback="true"
	else
		new_count="$(awk -v since="$watermark" 'NF && (since=="" || $0 > since)' "$feedback_times" | grep -c . || true)"
		[ "${new_count:-0}" -gt 0 ] && has_new_feedback="true"
	fi
	rm -f "$feedback_times"
fi

# ---- 5. Recommended lifecycle action ------------------------------------------------
# Only a high-confidence caught-up state early-noops; every uncertain case produces so
# the agent can make the real zero-delta / release-gate decision in Steps 2-3.
action="produce"
case "$classification" in
	blocked)
		action="noop" ;;
	none)
		action="produce" ;; # fresh
	ours|adopt)
		release_changed="false"
		[ "$release_ready" = "true" ] && [ "$upstream_release" != "$pr_recorded_release" ] && release_changed="true"
		# A published release on an open draft still owes the Step 3 release gate its
		# mark-ready (Step 6) transition, even once the tag is already recorded in the body
		# (e.g. a torn prior run refreshed the body but never flipped the draft, or the PR
		# was converted back to draft). Never let a caught-up state early-noop that pending
		# transition: produce so the agent can flip the draft to Ready.
		release_pending="false"
		[ "$release_ready" = "true" ] && [ "$pr_is_draft" = "true" ] && release_pending="true"
		if [ "$classification" = "ours" ] \
			&& [ -n "$upstream_sha" ] && [ -n "$pr_recorded_sha" ] \
			&& [ "$upstream_sha" = "$pr_recorded_sha" ] \
			&& [ "$has_new_feedback" != "true" ] \
			&& [ "$release_changed" != "true" ] \
			&& [ "$release_pending" != "true" ]; then
			action="noop"
		else
			action="produce"
		fi ;;
esac

pr_state="open"; [ -z "$pr" ] && pr_state="none"; write_target "$pr" "$pr_state" "$pr_is_draft" "$pr_recorded_sha" "$pr_recorded_release" "$classification" "$action"
step_summary "$classification" "$action" "${pr:-}" "$pr_recorded_sha" "$has_new_feedback"

echo "Setup: classification=${classification} action=${action} pr=${pr:-<none>} pr_branch=${PR_BRANCH:-<none>} recorded_sha=${pr_recorded_sha:-<none>} upstream_sha=${upstream_sha:-<none>} release_ready=${release_ready} new_feedback=${has_new_feedback}"
echo "-- target.json --"; jq '.' "$target_file"

# ---- 6. Detach HEAD so a fresh-path integration commit never advances the base branch ----
# The agent job is checked out on the base branch (main). On the fresh path the agent commits
# the integration and then creates the PR branch ref at that commit; if HEAD were still main,
# the commit would advance main itself and leave the new PR branch pointing at its own base,
# so create-pull-request would emit an empty patch (and, with threat-detection enabled, the
# run would hard-fail with no patch to screen). Detaching here -- deterministically, in this
# host script, rather than relying on the agent to run `git checkout --detach` -- keeps the
# base branch ref fixed at the checked-out commit. It is safe on every path: the incremental
# and adopt paths check out the existing PR branch afterward, and no path depends on being on
# a named branch. gh-aw's own "Checkout PR branch" step does not run for this workflow's
# workflow_call / workflow_dispatch triggers, so nothing re-attaches HEAD before the agent.
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
	if git checkout --detach --quiet; then
		echo "Detached HEAD at $(git rev-parse --short HEAD) so a fresh-path commit will not advance ${BASE_BRANCH}."
	else
		echo "::error::Failed to detach HEAD; refusing to continue because a fresh-path commit could advance ${BASE_BRANCH} and produce an empty patch."
		exit 1
	fi
else
	echo "::warning::Not inside a git work tree; skipping HEAD detach (expected only outside the workflow, e.g. in isolated script tests)."
fi
