#!/usr/bin/env bash
# Host-side setup for the "Agent Framework Template Worker" agentic workflow.
#
# Deterministically prepares everything the agent needs before it runs so the agent never has to
# discover, filter, de-duplicate, or build anything itself:
#   1. Resolves the target Agent Framework release (from the orchestrator's `target` input -- the
#      framework signal emitted by agent-framework-discover.cs -- or, on a standalone dispatch,
#      by running that discovery app here).
#   2. Maps the framework signal onto the aiagent-webapi template's package subset, reads the
#      versions currently pinned in eng/packages/ProjectTemplates.props, and computes whether main
#      needs a bump (the delta).
#   3. CI-validates the prospective bump: applies the desired versions to the props and the aligned
#      version to the template package project, restores + builds + packs the template package
#      through the repo's Arcade build, and runs the snapshot + execution tests -- recording whether
#      they succeeded. The working tree is left clean; the exact validated files are saved for the
#      agent to drop into place on the PR branch, so the agent never has to build.
#   4. Discovers the maintained draft PR and classifies it: OURS (carries our tracking marker),
#      ADOPT (a human-bootstrapped automation+area-ai-templates PR on our branch with no marker
#      yet), BLOCKED (a non-automation PR occupying our branch -- a human owns it), or NONE.
#   5. Reads the PR's recorded agent-framework-version and feedback-processed-through watermark,
#      detects review activity newer than the watermark (author-agnostic wake gate; never reads
#      comment bodies), and computes a recommended lifecycle action.
#
# Writes (under $AGENT_DIR, uploaded in the agent artifact):
#   target.json                     resolved target + template versions + PR discovery + action
#   ProjectTemplates.props.bumped    the validated, fully-bumped props file the agent drops in
#   build.log                        tail of the validation build (for the PR body / debugging)
#
# A transient API blip must never masquerade as "no PR / no work": discovery is retried and, when
# the maintained PR cannot be established with confidence, the action defaults to "produce".
#
# Environment:
#   GITHUB_REPOSITORY  owner/repo (set by Actions)
#   GH_TOKEN           token with pull-requests:read (the agent job's github.token)
#   TARGET_JSON        framework signal from the orchestrator (workflow_call); may be empty
#   AGENT_DIR          output dir (default /tmp/gh-aw/agent)
set -euo pipefail

AGENT_DIR="${AGENT_DIR:-/tmp/gh-aw/agent}"
REPO="${GITHUB_REPOSITORY:-}"
TARGET_JSON="${TARGET_JSON:-}"
mkdir -p "$AGENT_DIR"
target_file="$AGENT_DIR/target.json"

run_started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

DESIRED_BRANCH="update-agent-framework-template"
BASE_BRANCH="main"
# Labels the maintained PR must carry for the automation to own/act on it.
LABEL_A="automation"
LABEL_B="area-ai-templates"
# The state block delimiters -- a worker-internal concern. Whole yaml-comment lines
# `# ${STATE_MARKER}:state:begin` / `:state:end`.
STATE_MARKER="agent-framework-template"
PROPS="eng/packages/ProjectTemplates.props"
# The template NuGet package project, whose own version (Major/Minor/Patch, keeping the prerelease
# label) is aligned with the Agent Framework release on every bump. The shipped template itself is a
# .csproj-in whose ${PackageVersion:*} tokens resolve from PROPS at pack time, so a version bump
# only edits PROPS and this package project -- never the template content.
TEMPLATE_PKG_PROJ="src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.csproj"
# The template's snapshot + execution test project, run to confirm the template still works after the
# bump. It packs the template, runs `dotnet new aiagent-webapi`, then restores/builds the generated
# project -- so validation goes through the repo's Arcade build (build.sh), not a bare `dotnet build`.
TEST_PROJECT="test/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.IntegrationTests/Microsoft.Agents.AI.ProjectTemplates.Tests.csproj"
# Upstream Agent Framework repo whose dotnet-* releases describe what changed between versions.
UPSTREAM_REPO="microsoft/agent-framework"
DISCOVER="$(dirname "$0")/agent-framework-discover.cs"

# The template package itself ships on its own cadence and is excluded from the lockstep bump.
EXCLUDE_PKG="Microsoft.Agents.AI.ProjectTemplates"

# ---- helpers -----------------------------------------------------------------------
esc_re() { printf '%s' "$1" | sed 's/[.[\*^$/]/\\&/g'; }
get_pkg_version() { # file id
	local esc; esc="$(esc_re "$2")"
	sed -n -E "s|.*<PackageVersion Include=\"${esc}\" Version=\"([^\"]*)\".*|\1|p" "$1" | head -1
}
set_pkg_version() { # file id ver
	local esc; esc="$(esc_re "$2")"
	sed -i -E "s|(<PackageVersion Include=\"${esc}\" Version=\")[^\"]*(\")|\1${3}\2|" "$1"
}
# Template NuGet package version helpers. The version is Major.Minor.Patch plus a fixed prerelease
# label; the automation aligns Major/Minor/Patch with the release but never touches the label.
tmpl_prop() { sed -n -E "s|.*<$2>([^<]*)</$2>.*|\1|p" "$1" | head -1; }
tmpl_pkg_version() { # file -> e.g. 1.3.0-preview
	local maj min pat lbl
	maj="$(tmpl_prop "$1" MajorVersion)"; min="$(tmpl_prop "$1" MinorVersion)"
	pat="$(tmpl_prop "$1" PatchVersion)"; lbl="$(tmpl_prop "$1" PreReleaseVersionLabel)"
	printf '%s.%s.%s%s' "$maj" "$min" "$pat" "${lbl:+-$lbl}"
}
set_tmpl_pkg_version() { # file major minor patch (label preserved)
	sed -i -E "s|(<MajorVersion>)[^<]*(</MajorVersion>)|\1${2}\2|; \
	           s|(<MinorVersion>)[^<]*(</MinorVersion>)|\1${3}\2|; \
	           s|(<PatchVersion>)[^<]*(</PatchVersion>)|\1${4}\2|" "$1"
}

# ---- 1. Resolve the target framework release ---------------------------------------
if [ -z "$TARGET_JSON" ]; then
	# Standalone dispatch: run discovery here. It writes a single-element array; take element 0.
	# ImportDirectoryBuild{Props,Targets}=false isolates the file-based app from the repo's
	# Directory.Build.props (which injects analyzer PackageReferences that break the standalone build).
	scratch="$(mktemp)"
	if command -v dotnet >/dev/null 2>&1 && dotnet run "$DISCOVER" \
		--property:ImportDirectoryBuildProps=false --property:ImportDirectoryBuildTargets=false \
		-- "$scratch" >/dev/null 2>&1; then
		TARGET_JSON="$(jq -c '.[0]' "$scratch" 2>/dev/null || true)"
	fi
	rm -f "$scratch"
fi
if [ -z "$TARGET_JSON" ] || [ "$(jq -r 'type' <<<"$TARGET_JSON" 2>/dev/null || echo null)" != "object" ]; then
	echo "::error::No Agent Framework release signal available (empty or unparseable target). Refusing to continue."
	exit 1
fi

release_version="$(jq -r '.release_version // ""' <<<"$TARGET_JSON")"
release_date="$(jq -r '.release_date // ""' <<<"$TARGET_JSON")"
source_feed="$(jq -r '.source_feed // "dotnet-public"' <<<"$TARGET_JSON")"
if [ -z "$release_version" ]; then
	echo "::error::Framework signal is missing release_version. Refusing to continue."
	exit 1
fi

# Split the release into Major.Minor.Patch (the anchor release is always a stable X.Y.Z). These
# drive the template NuGet package's own version; its prerelease label is never changed.
rel_mmp="${release_version%%-*}"
rel_major="$(cut -d. -f1 <<<"$rel_mmp")"
rel_minor="$(cut -d. -f2 <<<"$rel_mmp")"
rel_patch="$(cut -d. -f3 <<<"$rel_mmp")"
rel_patch="${rel_patch:-0}"

# Discover every Microsoft.Agents.AI* package pinned in the props -- the Agent Framework dependencies
# that ship as a set and must be bumped in lockstep, each at its own tier -- excluding the template
# package itself. Data-driven so nothing is missed as the family grows.
mapfile -t AF_PKGS < <(grep -oE '<PackageVersion Include="Microsoft\.Agents\.AI[^"]*"' "$PROPS" \
	| sed -E 's/.*Include="([^"]+)".*/\1/' | grep -vx "$EXCLUDE_PKG" | sort -u)
if [ "${#AF_PKGS[@]}" -eq 0 ]; then
	echo "::error::No Microsoft.Agents.AI package versions found in ${PROPS}."
	exit 1
fi

# Map each AF package onto its at_release version from the signal -> desired_versions{ id: version }.
desired_versions="{}"
for id in "${AF_PKGS[@]}"; do
	ver="$(jq -r --arg s "$id" '.packages[$s].at_release // ""' <<<"$TARGET_JSON")"
	if [ -z "$ver" ] || [ "$ver" = "null" ]; then
		echo "::error::Framework signal has no at_release version for '${id}'."
		exit 1
	fi
	desired_versions="$(jq -c --arg id "$id" --arg v "$ver" '. + {($id): $v}' <<<"$desired_versions")"
done

# Current anchor version + whether any AF package differs from its target.
current_version="$(get_pkg_version "$PROPS" "Microsoft.Agents.AI" || true)"
main_needs_bump="false"
for id in "${AF_PKGS[@]}"; do
	cur="$(get_pkg_version "$PROPS" "$id" || true)"
	want="$(jq -r --arg id "$id" '.[$id]' <<<"$desired_versions")"
	[ "$cur" != "$want" ] && main_needs_bump="true"
done

# Template NuGet package version: align Major/Minor/Patch with the release, keep the prerelease label.
template_pkg_old=""
template_pkg_new=""
if [ -f "$TEMPLATE_PKG_PROJ" ]; then
	template_pkg_old="$(tmpl_pkg_version "$TEMPLATE_PKG_PROJ")"
	tmpl_label="$(tmpl_prop "$TEMPLATE_PKG_PROJ" PreReleaseVersionLabel)"
	template_pkg_new="${rel_major}.${rel_minor}.${rel_patch}${tmpl_label:+-$tmpl_label}"
	[ "$template_pkg_old" != "$template_pkg_new" ] && main_needs_bump="true"

	# Produce the bumped template package project for the agent to apply (Major/Minor/Patch aligned to
	# the release; prerelease label untouched). Pure version edit -- independent of the build below.
	cp "$TEMPLATE_PKG_PROJ" "$AGENT_DIR/ProjectTemplates.csproj.orig"
	cp "$TEMPLATE_PKG_PROJ" "$AGENT_DIR/ProjectTemplates.csproj.bumped"
	set_tmpl_pkg_version "$AGENT_DIR/ProjectTemplates.csproj.bumped" "$rel_major" "$rel_minor" "$rel_patch"
fi

# ---- 2. CI-validate the prospective bump (build the template package + run its tests) ---------
# Apply the bump to the working tree, then confirm the template still works through the repo's Arcade
# build: restore, build + pack the template package (producing the .nupkg the tests install), and run
# the snapshot + execution tests. The working tree is left clean afterward; the exact validated files
# are saved for the agent to drop onto the PR branch. If the build infrastructure cannot run (no repo
# SDK, feeds unreachable), validation is left inconclusive so the agent reports incomplete rather than
# shipping an unvalidated bump.
validated="false"
build_summary="not attempted"
tests_summary="not attempted"

if [ -f "./build.sh" ]; then
	cp "$PROPS" "$AGENT_DIR/ProjectTemplates.props.orig"
	for id in "${AF_PKGS[@]}"; do
		set_pkg_version "$PROPS" "$id" "$(jq -r --arg id "$id" '.[$id]' <<<"$desired_versions")"
	done
	cp "$PROPS" "$AGENT_DIR/ProjectTemplates.props.bumped"
	# Apply the template package version bump too, so the validation build reflects the full change.
	[ -f "$AGENT_DIR/ProjectTemplates.csproj.bumped" ] && cp "$AGENT_DIR/ProjectTemplates.csproj.bumped" "$TEMPLATE_PKG_PROJ"

	: >"$AGENT_DIR/build.log"
	build_ok="false"
	# Restore + build + pack the template package so the .nupkg the execution tests install exists.
	# Single-dash flags so eng/build.sh's -projects handler fires (realpath + no root-.sln fallback).
	if bash ./build.sh -ci -restore -build -pack \
		-projects "$PWD/src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.csproj" \
		-configuration Release >>"$AGENT_DIR/build.log" 2>&1; then
		build_ok="true"
		build_summary="template package restored, built, and packed against ${release_version}"
	else
		build_summary="template package restore/build/pack FAILED against ${release_version} (see build.log)"
		echo "::warning::${build_summary}"
	fi
	tail -80 "$AGENT_DIR/build.log" >"$AGENT_DIR/build.tail.log" 2>/dev/null || true

	# Snapshot + execution tests through Arcade (how CI runs them; picks up the packed template
	# nupkg from artifacts/packages). Only meaningful once the package built.
	tests_ok="false"
	if [ "$build_ok" = "true" ] && [ -f "$TEST_PROJECT" ]; then
		if bash ./build.sh -ci -restore -build -integrationTest \
			-projects "$PWD/$TEST_PROJECT" \
			-configuration Release >"$AGENT_DIR/tests.log" 2>&1; then
			tests_ok="true"
			tests_summary="snapshot + execution tests passed against ${release_version}"
		else
			tests_summary="snapshot/execution tests FAILED against ${release_version} (see tests.log)"
			echo "::warning::${tests_summary}"
		fi
		tail -80 "$AGENT_DIR/tests.log" >"$AGENT_DIR/tests.tail.log" 2>/dev/null || true
	elif [ "$build_ok" = "true" ]; then
		tests_summary="package built but tests could not run (no test project found); rely on PR CI"
	fi

	[ "$build_ok" = "true" ] && [ "$tests_ok" = "true" ] && validated="true"
	# Restore a clean working tree; the agent re-applies the bumped files on the PR branch.
	cp "$AGENT_DIR/ProjectTemplates.props.orig" "$PROPS"
	[ -f "$AGENT_DIR/ProjectTemplates.csproj.orig" ] && cp "$AGENT_DIR/ProjectTemplates.csproj.orig" "$TEMPLATE_PKG_PROJ"
else
	# No repo build script available. Still save bumped copies so the agent can proceed, but flag it.
	cp "$PROPS" "$AGENT_DIR/ProjectTemplates.props.bumped"
	for id in "${AF_PKGS[@]}"; do
		set_pkg_version "$AGENT_DIR/ProjectTemplates.props.bumped" "$id" "$(jq -r --arg id "$id" '.[$id]' <<<"$desired_versions")"
	done
	build_summary="repo build.sh not found on host; bump not CI-validated"
	tests_summary="repo build.sh not found on host; tests not run"
fi

# ---- helpers for PR discovery / tracking block -------------------------------------
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
	sed -n "s/^[[:space:]]*[-*+>]*[[:space:]]*${name}:[[:space:]]*//p" |
		head -1 | tr -d '"'\''\r' | sed 's/[[:space:]]*#.*$//; s/[[:space:]]*$//'
}
tracking_block() {
	awk -v b="# ${STATE_MARKER}:state:begin" -v e="# ${STATE_MARKER}:state:end" '
		{ t=$0; sub(/\r$/,"",t); gsub(/^[[:space:]]+|[[:space:]]+$/,"",t) }
		t == b { inb=1; buf=$0 ORS; next }
		inb    { buf=buf $0 ORS; if (t == e) { inb=0; last=buf } }
		END    { if (inb) last=buf; printf "%s", last }'
}
body_has_state_marker() {
	printf '%s' "$1" | jq -Rrs --arg m "# ${STATE_MARKER}:state:begin" \
		'split("\n") | any(gsub("^\\s+|\\s+$";"") == $m)'
}

write_target() { # $1=pr $2=pr_state $3=pr_is_draft $4=pr_recorded_version $5=classification $6=action
	jq -cn \
		--arg source_feed "$source_feed" --arg release_version "$release_version" \
		--arg release_date "$release_date" --arg current_version "${current_version:-}" \
		--argjson main_needs_bump "$main_needs_bump" \
		--argjson desired_versions "$desired_versions" \
		--argjson validated "$validated" --arg build_summary "$build_summary" \
		--arg desired_branch "$DESIRED_BRANCH" --arg base_branch "$BASE_BRANCH" \
		--arg pr_branch "${PR_BRANCH:-}" \
		--arg pr "$1" --arg pr_state "$2" --argjson pr_is_draft "${3:-false}" \
		--arg pr_recorded_version "$4" --arg classification "$5" --arg action "$6" \
		--argjson has_new_feedback "${has_new_feedback:-false}" \
		--arg watermark "${watermark:-}" --arg run_started_at "$run_started_at" \
		--arg props_path "$PROPS" \
		--arg template_pkg_proj "$TEMPLATE_PKG_PROJ" \
		--arg template_pkg_old "${template_pkg_old:-}" --arg template_pkg_new "${template_pkg_new:-}" \
		--arg tests_summary "${tests_summary:-}" --arg test_project "$TEST_PROJECT" \
		--arg upstream_repo "$UPSTREAM_REPO" \
		--arg from_version "${from_version:-}" --argjson af_change_count "${af_change_count:-0}" \
		--arg af_changes_path "${af_changes_rel:-}" \
		'{source_feed:$source_feed, release_version:$release_version, release_date:$release_date,
		  current_version:$current_version, main_needs_bump:$main_needs_bump,
		  desired_versions:$desired_versions, validated:$validated, build_summary:$build_summary,
		  template_pkg_proj:$template_pkg_proj, template_pkg_old:$template_pkg_old,
		  template_pkg_new:$template_pkg_new,
		  tests_summary:$tests_summary, test_project:$test_project,
		  upstream_repo:$upstream_repo, from_version:$from_version, af_change_count:$af_change_count,
		  af_changes_path:$af_changes_path,
		  desired_branch:$desired_branch, base_branch:$base_branch, pr_branch:$pr_branch,
		  pr:$pr, pr_state:$pr_state, pr_is_draft:$pr_is_draft,
		  pr_recorded_version:$pr_recorded_version, classification:$classification, action:$action,
		  has_new_feedback:$has_new_feedback, watermark:$watermark, run_started_at:$run_started_at,
		  props_path:$props_path}' >"$target_file"
}

step_summary() { # $1=classification $2=action $3=pr $4=pr_recorded_version $5=new_feedback
	{
		echo "## Agent Framework Template worker -- setup decision"
		echo ""
		echo "| field | value |"
		echo "|---|---|"
		echo "| source feed | \`${source_feed}\` |"
		echo "| release version | \`${release_version}\` |"
		echo "| current version (main) | \`${current_version:-<none>}\` |"
		echo "| main needs bump | ${main_needs_bump} |"
		echo "| template package version | \`${template_pkg_old:-<none>}\` -> \`${template_pkg_new:-<none>}\` |"
		echo "| bump CI-validated | ${validated} |"
		echo "| build | ${build_summary} |"
		echo "| tests | ${tests_summary:-<none>} |"
		echo "| Agent Framework changes to review | ${af_change_count:-0} release(s) ${from_version:+from ${from_version} }to ${release_version} |"
		echo "| maintained PR | ${3:-<none>} |"
		echo "| maintained PR branch | \`${PR_BRANCH:-<none>}\` |"
		echo "| PR recorded version | \`${4:-<none>}\` |"
		echo "| classification | **${1}** |"
		echo "| recommended action | **${2}** |"
		echo "| new review activity | ${5} |"
		[ "${feedback_query_failed:-false}" = "true" ] && echo "| review-activity query | **failed** -- wake gate opened |"
	} >>"${GITHUB_STEP_SUMMARY:-/dev/null}" 2>/dev/null || true
}

watermark=""
has_new_feedback="false"
feedback_query_failed="false"
from_version=""
af_change_count=0
af_changes_rel=""

# Gather the Agent Framework dotnet-* releases published strictly after $1 (from) and up to and
# including $2 (to), writing their notes to af-changes.md for the agent to evaluate consumption
# against. Version-range selection uses `sort -V` for SemVer-correct ordering. Best-effort: a feed
# hiccup leaves an empty change set rather than failing the run.
gather_af_changes() { # $1=from_version (may be empty) $2=to_version
	local from="$1" to="$2" out="$AGENT_DIR/af-changes.md" rel_json="" tag ver v_in_range
	af_changes_rel="af-changes.md"
	: >"$out"
	rel_json="$(gh api "repos/${UPSTREAM_REPO}/releases" --paginate 2>/dev/null \
		| jq -c '[.[] | select((.tag_name // "") | startswith("dotnet-"))
		         | {tag:.tag_name, name:.name, date:.published_at, body:.body}]' 2>/dev/null || true)"
	if [ -z "$rel_json" ] || [ "$rel_json" = "null" ]; then
		echo "_No Agent Framework release notes could be retrieved from ${UPSTREAM_REPO}._" >>"$out"
		af_change_count=0
		return 0
	fi

	{
		echo "# Agent Framework changes to review"
		echo ""
		echo "Releases from ${UPSTREAM_REPO} published ${from:+after \`${from}\` and }up to \`${to}\`."
		echo "Evaluate each for API changes, deprecations, and newly prescribed patterns, then update"
		echo "the template and any other Agent Framework consumption to match."
		echo ""
	} >>"$out"

	local count=0 i len
	len="$(jq 'length' <<<"$rel_json")"
	# Releases are newest-first from the API; emit oldest-first for readability.
	for (( i=len-1; i>=0; i-- )); do
		tag="$(jq -r ".[$i].tag" <<<"$rel_json")"
		ver="${tag#dotnet-}"
		# Keep versions v with: from < v <= to  (skip from itself; include to).
		if [ -n "$from" ]; then
			[ "$ver" = "$from" ] && continue
			[ "$(printf '%s\n%s\n' "$from" "$ver" | sort -V | tail -1)" = "$ver" ] || continue
		fi
		[ "$(printf '%s\n%s\n' "$ver" "$to" | sort -V | tail -1)" = "$to" ] || continue
		count=$((count + 1))
		{
			echo "## ${tag} ($(jq -r ".[$i].date" <<<"$rel_json"))"
			echo ""
			jq -r ".[$i].body // \"(no release notes)\"" <<<"$rel_json"
			echo ""
		} >>"$out"
	done
	af_change_count=$count
	[ "$count" -eq 0 ] && echo "_No Agent Framework releases fall in this range._" >>"$out"
	return 0
}

if [ -z "$REPO" ] || [ -z "${GH_TOKEN:-}" ]; then
	echo "GITHUB_REPOSITORY or GH_TOKEN unset; cannot discover PR -- defaulting to produce (fresh) if a bump is needed"
	act="noop"; [ "$main_needs_bump" = "true" ] && act="produce"
	write_target "" "" false "" "none" "$act"
	step_summary "none" "$act" "" "" "false"
	exit 0
fi

# ---- 4. Discover the maintained PR and classify it ---------------------------------
BASE_OWNER="${REPO%%/*}"
pr="" PR_BRANCH="" pr_is_draft="false" classification="none" body_ok="false"
for attempt in 1 2 3; do
	rows="$(gh api --method GET "repos/${REPO}/pulls" \
		-f state=open -f head="${BASE_OWNER}:${DESIRED_BRANCH}" -f base="${BASE_BRANCH}" -f per_page=100 2>/dev/null \
		| jq -c --arg owner "$BASE_OWNER" --arg branch "$DESIRED_BRANCH" --arg base "$BASE_BRANCH" \
			'[.[] | select((.head.repo.owner.login // "") == $owner and .head.ref == $branch and (.base.ref // "") == $base)
			  | {number, headRefName:.head.ref, isDraft:(.draft // false), labels:(.labels // []), updatedAt:.updated_at}]
			 | sort_by(.updatedAt) | reverse' 2>/dev/null || true)"
	[ -n "$rows" ] && break
	[ "$attempt" -lt 3 ] && sleep 2
done
rows="${rows:-[]}"

pr="$(jq -r '(.[0].number) // empty' <<<"$rows" 2>/dev/null || true)"
if [ -n "$pr" ]; then
	pr_is_draft="$(jq -r '.[0].isDraft // false' <<<"$rows")"
	PR_BRANCH="$(jq -r '.[0].headRefName // ""' <<<"$rows")"
	has_labels="$(jq -r --arg a "$LABEL_A" --arg b "$LABEL_B" '[.[0].labels[]?.name] | (index($a) != null) and (index($b) != null)' <<<"$rows")"
	fetch_pr_body "$pr"
	has_marker="$(body_has_state_marker "$body")"
	if [ "$body_ok" != "true" ] && [ "$has_labels" = "true" ]; then
		classification="ours"
	elif [ "$has_labels" = "true" ] && [ "$has_marker" = "true" ]; then
		classification="ours"
	elif [ "$has_labels" = "true" ] && [ "$has_marker" != "true" ] && [ "$pr_is_draft" = "true" ]; then
		# Human-bootstrapped: automation labels + draft + no tracking marker yet -> adopt it.
		# Both the label and draft gates must pass; a labeled but non-draft (ready) PR is left to
		# the human (falls through to blocked).
		classification="adopt"
	else
		classification="blocked"
	fi
fi

# ---- 5. Read recorded state + wake gate + action -----------------------------------
pr_recorded_version=""
if [ -n "$pr" ] && [ "$classification" != "blocked" ] && [ "$body_ok" = "true" ]; then
	watermark="$(printf '%s\n' "$body" | tracking_block | tracking_value "feedback-processed-through")"
	pr_recorded_version="$(printf '%s\n' "$body" | tracking_block | tracking_value "agent-framework-version")"
fi

# The evaluation baseline is what the repo already integrates: the maintained PR's recorded version
# when we own one, otherwise main's current version. Gather the Agent Framework changes from there to
# the target release for the agent to review consumption against.
from_version="$current_version"
{ [ "$classification" = "ours" ] || [ "$classification" = "adopt" ]; } && [ -n "$pr_recorded_version" ] && from_version="$pr_recorded_version"
gather_af_changes "$from_version" "$release_version"

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
		echo "::warning::review-activity query failed for PR #${pr} after retries; opening the wake gate"
		has_new_feedback="true"
	else
		new_count="$(awk -v since="$watermark" 'NF && (since=="" || $0 > since)' "$feedback_times" | grep -c . || true)"
		[ "${new_count:-0}" -gt 0 ] && has_new_feedback="true"
	fi
	rm -f "$feedback_times"
fi

# Recommended lifecycle action. The agent's Step 3 remains authoritative and refines edge cases.
action="produce"
case "$classification" in
	blocked)
		action="noop" ;;
	none)
		# Fresh only if main actually needs the bump; otherwise the template is already current.
		[ "$main_needs_bump" = "true" ] && action="produce" || action="noop" ;;
	ours|adopt)
		# Caught up (PR already at this release) with no new feedback -> no-op; else produce.
		if [ "$classification" = "ours" ] && [ "$pr_recorded_version" = "$release_version" ] && [ "$has_new_feedback" != "true" ]; then
			action="noop"
		else
			action="produce"
		fi ;;
esac

write_target "$pr" "open" "$pr_is_draft" "$pr_recorded_version" "$classification" "$action"
step_summary "$classification" "$action" "$pr" "$pr_recorded_version" "$has_new_feedback"
echo "Setup complete: classification=${classification} action=${action} release=${release_version} validated=${validated}"
