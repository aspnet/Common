# Servicing Release Preparation (Patch Releases)

Use this flow for patch releases (`<major>.<minor>.<patch>`) prepared directly on the latest public `release/<major>.<minor>` branch.

This is a **different track** from Stage 1/2 internal-branch prep. Do not mix them unless the user explicitly overrides.

## Outcome

Produce a servicing-prep PR into `release/<major>.<minor>` that includes:

1. Patch-version bump commit.
2. Cherry-picks of user-selected `main` commits (in `main` merge order).
3. Any required release-branch-specific touch-ups.

Then stop for merge/approval before publish steps.

## Step 1: Confirm target branch and patch version

1. Confirm the target public branch (`release/<major>.<minor>`).
2. Confirm the target patch version (`<major>.<minor>.<patch>`).
3. Create a working branch from `origin/release/<major>.<minor>`.

Use [the canonical checkpoint/approval contract](../../references/workflow.md). Resolve the public remote by URL rather than assuming `origin`. Record old/new patch, intended release date, baseline SHA and selected package scope. Apply the [per-line version guard](stage-2-update-dependencies.md#validate-the-release-premise-before-mutation): patch jumps larger than one need explicit concern/confirmation. Product version properties are manual; check evaluated package/assembly versions and later compare official artifact versions.

## Step 2: Survey the release branch for existing backport activity

Before building the full candidate list, check for evidence of existing backport work targeting this
release. This step is informational -- it seeds the candidate list and reminds the user that the release-manager agent can efficiently
cherry-pick from `main` as part of the servicing release process.

### 2a) Identify backport PRs

1. **Open PRs** targeting `release/<major>.<minor>`: find any with titles starting with
   `[release/<major>.<minor>]` or bodies containing `Backport of #NNNN` / `backport of #NNNN`.
2. **Recently merged PRs** into `release/<major>.<minor>` since the previous release tag: apply the
   same pattern match.
3. For each backport PR found, extract the source `main` PR number from its title or body.

### 2b) Classify backport PRs by release status

For each source `main` PR number identified, check whether it already shipped in a previous release:

- Because servicing releases typically cherry-pick (new SHAs), search the release-branch history at `<previous-tag>` for the PR number/backport markers (for example `git log <previous-tag> --grep "#NNNN"`).
- Or check whether the PR number appears in a prior release's release notes body.

Classify each backport PR as either:

- **Already released** -- source PR shipped in a prior release tag; carry this into Step 3d for the
  already-released grounding table (do not present it as a selectable candidate or comment on it).
- **Not yet released** -- source PR has not shipped; carry it into the Step 3 candidate list as a
  pre-highlighted row.

### 2c) Inform the user if open or unshipped backport PRs exist

If any open or recently-merged (but not yet incorporated) backport PRs remain after filtering:

> "I see backport PRs already open/merged targeting `release/<major>.<minor>`. The release manager
> can cherry-pick those changes directly from `main` -- this is typically more efficient than
> managing individual backport PRs. I'll include the corresponding `main` PRs as pre-highlighted
> candidates in the selection below."

Carry the source `main` PR numbers into the Step 3 candidate list as pre-highlighted rows.

If no relevant backport activity is found, proceed directly to Step 3 without comment.

---

## Step 3: Build the candidate backport list from `main`

The user needs a clear **selection list** of commits from `main` that are not yet included in `release/<major>.<minor>`.

For grounding, also present a separate view of what is already merged into `release/<major>.<minor>` (whether those merges happened before or after the target patch-version bump point).

### 3a) Gather candidate PRs/commits

Build candidates from commits in `main` that are not in `release/<major>.<minor>`, then map them to PR numbers where possible.

### 3b) Build grounding for already-merged release-branch items

Identify items that already shipped on the release branch by checking merged PRs into `release/<major>.<minor>` and harvesting referenced `#NNNN` slugs from their titles/bodies.

Present these as **grounding only** (not selectable candidates). If a patch-bump commit for the target patch exists on the release branch, label each grounding row as:

- `before patch bump`, or
- `after patch bump`

If no patch-bump commit exists yet on the release branch, label rows as `before planned patch bump`.

### 3c) Enrich each candidate with affected libraries

For each candidate PR:

1. Read changed files.
2. Derive affected libraries from touched paths (for example, `src/Libraries/<LibraryName>/...`).
3. Keep the list unique and sorted.

### 3d) Present the selection table

Show a selection table with one row per **selectable** candidate item. Pre-highlight any rows
seeded from Step 2 (existing backport PRs), so the user can quickly confirm or adjust those:

| SHA (short) | Title | PR | Libraries affected | Pre-selected? |
|---|---|---|---|---|
| `abc1234` | Commit/PR title | `#1234` link | `Microsoft.Extensions.AI`, ... | ✔ (backport PR open) |

Then show a separate grounding table for items already merged into the release branch for this
release cycle:

| Timing vs patch bump | SHA (short) | Title | PR | Libraries affected |
|---|---|---|---|---|
| before patch bump / after patch bump / before planned patch bump | `def5678` | Commit/PR title | `#5678` link | `Microsoft.Extensions.AI.Abstractions`, ... |

Then show a third table for items identified in Step 2 as already shipped in a **prior** release:

| Prior release tag | Source PR | Title | Libraries affected |
|---|---|---|---|
| `v10.8.1` | `#5432` link | Commit/PR title | `Microsoft.Extensions.AI`, ... |

Requirements:

- Keep already-merged release-branch rows out of the selectable table.
- Prefer PR-number rows when available.
- If a commit has no PR number, mark it and ask the user whether to include it explicitly.

## Step 4: Ask the user to choose items (multi-select)

Prompt the user to select rows from the **selectable candidate table only** (for example by PR numbers or row numbers, comma-separated).

Do not cherry-pick until selection is explicit.

## Step 5: Confirm selected roots and provisional dependency closure

Derive the selected roots from the chosen commits and explicit human additions, then follow
[package scope and dependency validation](../../references/package-scope-and-dependency-validation.md)
to approve and record the provisional manifest in the servicing-prep PR.

## Step 6: Apply commits in servicing order

Apply commits in this exact order:

1. **Bump patch version first**.
2. **Cherry-pick selected commits** in the order they merged into `main`.
3. **Apply touch-ups** needed only for the servicing release branch.

For each cherry-pick, record whether it was:

- **Clean cherry-pick** (no merge conflicts), or
- **Cherry-pick with merge conflicts** (include conflict-resolution notes).

### Patch version bump details

- Update `eng/Versions.props` patch version for the target release.
- Commit message format: `Bump version to <major>.<minor>.<patch>`.
- Review and obtain approval before this separate commit and each backport/touch-up commit; propagate release-owned trailer policy to children and inspect all outgoing messages before push.
- This matches recent 10.* servicing examples (`10.4.1`, `10.5.1`, `10.5.2`, `10.8.1`).

## Step 7: Create the servicing-prep PR

Do not push or open the PR until the user explicitly says to do so.

When creating the PR into `release/<major>.<minor>`, apply the `DO-NOT-SQUASH` label at creation time.

PR title format:

`Prepare <major>.<minor>.<patch> Servicing Release`

PR body format:

```md
Prepares the <major>.<minor>.<patch> servicing release.

## Selected packages
- <package-name> (<version>) — <independent change/selection reason>
- <package-name> (<version>) — <independent change/selection reason>

## Dependency-closure packages
- <package-name> (<version>) — required by <parent-package> (<exact dependency requirement>)
- None

The dependency closure is provisional until recomputed from the official build's PackageArtifacts.

## Packages in the complete provisional release scope
- <package-name> (<version-if-different-or-prerelease>)
- <package-name> (<version-if-different-or-prerelease>)

## Commits included
- Bump version to <major>.<minor>.<patch>
- Clean cherry-pick #<pr-number>
- Cherry-pick with merge conflicts #<pr-number>
  - Conflict: <file-or-area>; Resolution: <how it was resolved>
  - Conflict: <file-or-area>; Resolution: <how it was resolved>
```

Notes:

- Use `#<pr-number>` slugs for included PRs; GitHub renders number/title automatically.
- Keep commit order in the PR aligned with Step 6.
- For every conflicted cherry-pick, include one or more indented sub-bullets describing the conflict(s) and resolution(s).
- If a selected commit has no PR number, use its short SHA in place of `#<pr-number>`.
- Keep the `DO-NOT-SQUASH` label on the PR from creation through merge.
- For the merge method into `release/<major>.<minor>`, prefer **Rebase and merge** (never squash).

## Step 8: Preserve scope for downstream stages

Keep the PR description current if selected roots change: recompute the provisional closure and
obtain approval again. Publish-release replaces it with the approved artifact-derived final scope.

## After this preparation

After the servicing-prep PR merges into `release/<major>.<minor>`, continue with **publish-release** servicing flow:

1. wait for mirror into AzDO,
2. run `extensions-ci-official` from `release/<major>.<minor>`,
3. recompute closure from official artifacts and publish selected plus dependency-closure packages,
4. continue post-release checks and notes.
