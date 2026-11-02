# Prepare Release

Prepares a `dotnet/extensions` release for publication. This playbook supports **two preparation tracks**:

- **Monthly release** -- prepare a public branch (`release/<major>.<minor>`) for the internal release flow on `internal/release/<major>.<minor>`.
- **Servicing release** -- prepare directly on `release/<major>.<minor>` by selecting backports from `main` and bumping patch version.

## Choose the preparation track first

Load and present [the canonical workflow and checkpoint](../references/workflow.md) first.

Before making changes, determine which track applies:

| Release type | Use this path |
|---|---|
| Monthly release | Stages 1-2 in this README |
| Servicing release (`<major>.<minor>.<patch>`) | [references/stages-1-2-servicing-branch.md](references/stages-1-2-servicing-branch.md) |

Do **not** mix tracks. Servicing releases should not run Stage 1/2 internal-branch prep unless the user explicitly asks to override the servicing workflow.

## Stage workflow (monthly release)

Follow the canonical dependencies. Treat each file-changing stage/sub-stage as an independent committable unit. For each:

1. Apply the changes described in the stage's reference file.
2. Prompt the user to review the changes (summarize what changed and show the diff).
3. Wait for explicit approval for that commit; no sub-stage auto-commits.
4. Create a single commit that contains only that stage's (or sub-stage's) changes.

Every stage gets its own commit, and every sub-stage gets its own commit. Never combine multiple stages or sub-stages into a single commit. Never push until the user explicitly instructs it.

Commits this playbook creates land in public dotnet/extensions history -- they flow out later through the non-squash internal-to-public merge (validate-release, Stage 6). Keep the `Co-authored-by: Copilot` trailer on those commits but omit the `Copilot-Session` trailer.

## Stage 0 - Capture and cut the public baseline (monthly release)

Before internal preparation, confirm the intended month and product version, select the original public `main` commit that will ship, and record its full SHA and public remote URL. Do not later snap `release/<major>.<minor>` from a newer `main` containing unshipped changes.

Obtain explicit approval to create public `release/<major>.<minor>` at that SHA. If it already exists, verify its identity/history rather than resetting it. Protected branch creation normally requires the human's account elevation/JIT; explain this before the handoff. Provide an exact owning-worktree/SHA/destination command for a normal authorized push. Effective GitHub credentials can still be rejected after authorization: hand elevation/push back to the human concisely, then verify the remote ref. Never change protection rules, force-push, or escalate/bypass permissions. Do not proceed as if the branch exists until verified. Base internal staging on this captured baseline, not an independently selected HEAD.

## Stage 1a - Prepare Internal Branch (monthly release)

Apply internal infrastructure and stabilization changes as a separate approved commit. Do not change product numbers in this substep; product versions are manual in Stage 1b, while dependency versions are updated with darc in Stage 2.

Read and follow [references/stage-1-prepare-internal-branch.md](references/stage-1-prepare-internal-branch.md).

## Stage 1b - Bump the product version (monthly release)

Inspect `MajorVersion`, `MinorVersion`, `PatchVersion` in `eng/Versions.props`, show old -> intended new product version and expected release date, and apply the version-line guard in [Stage 2](references/stage-2-update-dependencies.md#validate-the-release-premise-before-mutation). These product properties are **manual**, not Dependency Flow updates. Update them explicitly and review/approve a separate `Bump version to <major>.<minor>.<patch>` commit before dependencies. Check evaluated PackageVersion/AssemblyVersion across relevant package variants/TFMs (including preview/template exceptions); do not infer them solely from the three source properties. Record evaluation commands/results or an actual restore/tooling blocker; compare official artifact versions again before publishing.

## Stage 2 - Update Dependencies (monthly release)

Update the branch's product dependencies to the pending .NET 9, .NET 8, and .NET 10 servicing releases using `darc update-dependencies`. The BAR build IDs come from the release.dot.net Release Tracker, which is behind Microsoft auth and unreachable by the agent, so the user supplies them (pasted `ReleaseManifest.json` or a downloaded copy). This stage has three sub-stages, each its own commit: .NET 9, then .NET 8, then .NET 10.

Read and follow [references/stage-2-update-dependencies.md](references/stage-2-update-dependencies.md).

## Servicing preparation workflow (servicing release)

For servicing releases, do not use Stage 1/2 above. Instead, follow:

- [references/stages-1-2-servicing-branch.md](references/stages-1-2-servicing-branch.md)

That flow covers commit selection from `main`, patch-version bumping, servicing PR composition, and package-scope confirmation for later publish/validate/release-notes stages.

## Next

- Monthly release: once Stages 1-2 are committed and reviewed, continue with **publish-release** (land, publish, promote) and **validate-release** (verify symbols, reconcile branches).
- Servicing release: once the servicing prep PR merges into `release/<major>.<minor>`, continue with **publish-release** (mirror + official build + publish selected scope), then **validate-release** and **write-release-notes**.
