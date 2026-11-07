# Stage 3 - Build (Servicing Release)

Use this Stage 3 variant for servicing releases prepared directly on `release/<major>.<minor>`.

Unlike the monthly release flow, there is no `stage-release-*` -> `internal/release/*` landing step. The servicing-prep PR merges into the public release branch first, then that branch is mirrored to AzDO.

## Prerequisites

- Load [candidate identity, build/test and retry gates](../../references/candidate-validation.md).

- The servicing-prep PR into `release/<major>.<minor>` is merged.
- You know the selected package roots and provisional closure from that PR description.
- You have access to Azure DevOps and the `extensions-ci-official` pipeline.

## Steps

1. Confirm the merged commit on `release/<major>.<minor>`.
2. Wait for that commit to mirror into AzDO's `dotnet-extensions` repository.
3. Verify the mirror's exact SHA; obtain queue approval with explicit `runTests` policy and exclusions, then trigger `extensions-ci-official` from that pinned release ref.
4. Wait for a successful official run producing release `PackageArtifacts`.
5. Record and share with the user:
   - AzDO build run URL
   - AzDO build ID / build number
   - BAR build ID for the official release build
6. Derive the authoritative artifact manifest from the official `PackageArtifacts` `.nuspec` files
   by following [package scope and dependency validation](../../references/package-scope-and-dependency-validation.md).
7. Reconcile it with the PR's provisional manifest and obtain explicit approval before Stage 4. Check actual test counts/failures, jobs and logs, not only exit code/green run. Tests disabled, blocked restore and skipped integrations remain explicit limitations.

## Notes

- This stage is operational and irreversible once publishing starts downstream.
- Do not proceed to Stage 4 publishing until the official run is green and artifact identity (commit + BAR id) is confirmed.
- Do not push/merge anything from this stage unless the user explicitly asks.

## After the stage

Proceed to **Stage 4 - Publish and Promote** using:

- the official release build from this stage, and
- the selected roots captured in the merged servicing-prep PR description, and
- the approved artifact-derived dependency closure from this stage.
