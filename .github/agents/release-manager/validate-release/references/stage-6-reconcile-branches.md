# Stage 6 - Reconcile Branches

After recording Stage 5's consumer/msdl metadata results, publish cleaned source history to the public release branch so full Source Link URL/checksum verification can run. Then reconcile the actual public release PR merge into `main`. Follow [the canonical dependencies](../../references/workflow.md); do not block source publication on URLs that cannot exist until this step.

This stage is designed for the **monthly release**. For servicing releases prepared directly on public `release/<major>.<minor>`, Stage 6 is usually skipped unless the user explicitly asks for additional branch-flow reconciliation.

The agent stages merges on owned `merge/*` branches, applies cleanup and shows the diff **before** requesting commit approval. Commit, report SHA, then wait for separate push instructions. PR completion is separately human-gated; use authorized merge-commit permissions/elevation, never change repository settings or bypass rules. Each merge PR lands as a merge commit, never squashed.

Run each sub-stage only when the user instructs it. Sub-stage 2 depends on Sub-stage 1's PR having already merged into the public release branch.

## Prerequisites

- Stage 4 complete: packages published and the release is public.
- The original public baseline/remote release ref captured in Stage 0 is verified. Do not create the release branch from today's `main`. Pin the official shipped SHA, not later internal HEAD.
- A clone with the public GitHub remote (referred to here as `dotnet`) and up-to-date `internal/release/<major>.<minor>`, `release/<major>.<minor>`, and `main` branches. Fetch all three first.

## Sub-stage 1 - Merge internal release branch into the public release branch

Carry the version bumps and internal product changes out to the public `release/<major>.<minor>` branch while discarding the internal-only infrastructure changes (the Stage 1 prep).

1. From `release/<major>.<minor>`, create the merge branch (do not use a `release/` prefix -- it is protected):

   ```
   git switch release/<major>.<minor>
   git switch -c merge/<major>.<minor>
   ```

2. Stage the merge without committing:

   ```
   git merge --no-ff --no-commit <official-build-source-sha>
   ```

3. Derive the **complete** internal-only file/hunk list from actual prep commits and captured baseline blobs, then review it. It can include `RestoreAndBuild.yml`, not only `BuildAndTest.yml`. Restore only internal infrastructure/auth/private-feed hunks to the original public baseline; preserve approved product/dependency/version changes in shared files. A hardcoded restore list or "keep everything else" is insufficient. Preserve unrelated user edits.

4. Compare the **whole public tree** to the shipped source tree and captured public baseline. Account for every difference: only reviewed infrastructure exclusions and intended product/dependency/version content may remain. Scan for private feeds/auth/infra leakage. Public history preserves the shipped SHA via merge; this cleaned tree's merge SHA is **not** the release tag target.

5. Compare against the last five releases to check for anomalies vs. consistency. Look at the corresponding merge PRs from the previous five monthly releases (their titles vary -- e.g. "Merge published release into release/X.Y", "Merging internal changes into the release/X.Y branch", "Merge internal changes from X.Y"). Find them and list their changed files with `gh`:

   ```
   gh pr list --repo dotnet/extensions --base release/<earlier>.<minor> --state merged --json number,title
   gh pr view <number> --repo dotnet/extensions --json files
   ```

   Compare this merge's staged file set (`git diff --cached --stat`) against them and flag anomalies to the user before committing:
   - `eng/Version.Details.xml` and `eng/Versions.props` appear in every release and must be here too (including the stabilization flip).
   - Internal-only infrastructure/auth hunks must be absent, including whichever pipeline templates were actually edited. Whole-file exclusions must not erase approved dependency/feed cleanup.
   - Tooling files such as `.github/agents/**` and `.github/skills/**` are excluded by recent releases -- flag if present.
   - The remaining files should be version bumps plus intentional product/test backports. Flag anything unexpected: an unusually large or empty change set, missing versioning, leaked infrastructure, or files no prior release touched.

6. Review the staged diff: it should be version bumps + product changes, with **no** infrastructure changes. Then commit:

   ```
   git commit -m "Merge published release into release/<major>.<minor>"
   ```

7. Pushing (user-directed) to the URL-resolved public remote and opening the PR into `release/<major>.<minor>` -- title "Merge published release into release/<major>.<minor>", label `DO-NOT-SQUASH`, description "Merge using a merge commit. Do not squash." Do not enable auto-merge (squash). The human completes it using authorized elevation/permissions.

Before handoff, name owning session/worktree/path, branch, SHA, destination URL/ref and PR base; perform the workflow's prepush identity checks in that exact worktree. After merge, verify the official build source commit is publicly reachable and resume Stage 5b's full URL/checksum check.

## Sub-stage 2 - Merge the public release branch into main

Do this only after Sub-stage 1's PR has merged into `release/<major>.<minor>`. It reverts the stable-versioning flip so `main` does not produce stable/release versions.

Speculative preparation is allowed, but fetch and rebuild/refresh against the **actual release PR merge commit** before committing. Never substitute a local speculative merge SHA or later unshipped branch HEAD for that merge.

1. Fetch, then from `main` create the merge branch:

   ```
   git switch main
   git switch -c merge/<major>.<minor>-to-main
   ```

2. Stage the merge without committing:

   ```
   git merge --no-ff --no-commit <actual-release-pr-merge-sha>
   ```

3. In `eng/Versions.props`, revert only the package-stabilization change (keep all version numbers and all `eng/Version.Details.xml` changes):
   - Set `StabilizePackageVersion` back to `false`.
   - Set `DotNetFinalVersionKind` back to empty (`<DotNetFinalVersionKind />`).
   - `git add eng/Versions.props`.

4. Review the staged diff, then commit:

   ```
   git commit -m "Merge release/<major>.<minor> into main"
   ```

5. Pushing (user-directed) to `dotnet` and opening the PR into `main` -- title "Merge release/<major>.<minor> into main", label `DO-NOT-SQUASH`, description "Merge using a merge commit. Do not squash." Do not enable auto-merge (squash). The user completes the PR.

## After the stage

This stage produces merge commits on `merge/*` branches but does not push or complete the pull requests. Tagging and publishing the release notes are handled by the **write-release-notes** playbook. Next, confirm the support-page listing (Stage 7).
