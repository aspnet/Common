# Evaluating Agent Framework changes across a release

A version bump is not complete until the template and any other Agent Framework consumption in the
repository are confirmed to still work and to follow the currently prescribed patterns.

## Inputs the host prepares

The worker's setup script writes these into `/tmp/gh-aw/agent/` and records them in `target.json`:

- `af_changes_path` (`af-changes.md`) -- the `microsoft/agent-framework` `dotnet-*` release notes for
  every release published after `from_version` and up to `release_version` (`af_change_count` of
  them). `from_version` is the version the repo already integrated (the maintained PR's recorded
  version, or `main`'s current version).
- `build_summary` / `tests_summary` -- the host build + test results for the prospective bump.

## What to evaluate

1. Read `af-changes.md`. Identify: removed or renamed APIs, deprecations, behavioral changes, and any
   newly recommended/prescribed patterns or replacements.
2. Enumerate the consumption:
   - the template under `src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/**`, and
   - other consumption under `src/Libraries/Microsoft.Extensions.AI*/**`.
3. For each affected usage, update it to the current prescribed pattern, staying within the allowed
   files. Prefer the minimal change that adopts the new guidance.
4. If nothing is affected, that is a valid result -- record in the PR body that the changes were
   reviewed and no consumption updates were required.

## Scope discipline

Only edit the allowed files: the template tree, the `Microsoft.Extensions.AI*` libraries,
`eng/packages/ProjectTemplates.props`, and the template integration-test tree. Anything else (or a
change that would require touching out-of-scope code) is out of scope -- note it in the PR body for a
human rather than editing it.
