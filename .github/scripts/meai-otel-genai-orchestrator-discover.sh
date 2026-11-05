#!/usr/bin/env bash
# meai-otel-genai-orchestrator-discover.sh
#
# Deterministic discovery for the MEAI Otel GenAI Orchestrator. The gen-ai integration is
# a SINGLE evergreen work unit -- one draft PR that tracks the upstream
# open-telemetry/semantic-conventions-genai repository into Microsoft.Extensions.AI --
# so discovery emits AT MOST ONE target. Its whole job is to resolve the upstream ref
# to scan into a concrete commit SHA (so the worker never guesses) and hand the
# worker a fully specified target. All produce/no-op/adoption decisions are made
# later, in the worker setup, from the resolved SHA.
#
# A transient API blip must never masquerade as "no upstream" (which would silently
# drop the target and skip the run), so the resolve is retried; an unresolvable ref is
# a hard error (exit 1), never an empty target set.
#
# Requires `gh` (GH_TOKEN) and `jq`. Emits a JSON array (0 or 1 target) to stdout.
#
# Environment:
#   UPSTREAM_REPO  optional  upstream repo (default open-telemetry/semantic-conventions-genai)
#   UPSTREAM_REF   optional  ref to scan (branch/tag/SHA); empty = default-branch HEAD
set -euo pipefail

UPSTREAM_REPO="${UPSTREAM_REPO:-open-telemetry/semantic-conventions-genai}"
UPSTREAM_REF="${UPSTREAM_REF:-}"

# resolve_sha <ref-or-empty> -> concrete commit SHA (retried); empty on authoritative miss.
resolve_sha() {
	local ref="$1" sha="" attempt
	for attempt in 1 2 3; do
		if [ -z "$ref" ]; then
			# Default-branch HEAD.
			local default_branch
			default_branch="$(gh api "repos/${UPSTREAM_REPO}" -q '.default_branch' 2>/dev/null || true)"
			[ -n "$default_branch" ] && sha="$(gh api "repos/${UPSTREAM_REPO}/commits/${default_branch}" -q '.sha' 2>/dev/null || true)"
		else
			sha="$(gh api "repos/${UPSTREAM_REPO}/commits/${ref}" -q '.sha' 2>/dev/null || true)"
		fi
		[ -n "$sha" ] && { printf '%s' "$sha"; return 0; }
		echo "::warning::could not resolve '${ref:-<default HEAD>}' in ${UPSTREAM_REPO} (attempt ${attempt}/3)" >&2
		[ "$attempt" -lt 3 ] && sleep 2
	done
	return 0
}

upstream_sha="$(resolve_sha "$UPSTREAM_REF")"
if [ -z "$upstream_sha" ]; then
	echo "::error::Could not resolve upstream ref '${UPSTREAM_REF:-<default HEAD>}' in ${UPSTREAM_REPO} after retries. Aborting instead of emitting an empty target set (a transient blip must never silently skip the run)." >&2
	exit 1
fi

echo "::notice::${UPSTREAM_REPO} scan ref = ${UPSTREAM_REF:-<default HEAD>} -> ${upstream_sha}" >&2

# The gen-ai integration uses a single evergreen branch; the tracked version and scan SHA
# live in the worker's PR body state block, never in the branch (so the one PR refreshes
# forever as upstream advances). The state block's marker is a worker-internal detail --
# the orchestrator neither emits nor knows it.
target="$(jq -cn \
	--arg upstream_repo "$UPSTREAM_REPO" \
	--arg upstream_ref "$UPSTREAM_REF" \
	--arg upstream_sha "$upstream_sha" \
	--arg desired_branch "update-otel-genai-to-latest" \
	'{upstream_repo:$upstream_repo, upstream_ref:$upstream_ref, upstream_sha:$upstream_sha,
	  desired_branch:$desired_branch}')"

jq -cn --argjson t "$target" '[$t]'
