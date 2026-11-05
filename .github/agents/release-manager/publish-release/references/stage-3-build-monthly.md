# Stage 3 - Build (Monthly Release)

After the prepare-release playbook is committed and reviewed (Prepare Internal Branch, then Update Dependencies), land the `stage-release-<major>.<minor>` branch on `internal/release/<major>.<minor>` -- gated on a green official build.

This Stage 3 reference is for the **monthly release**. Servicing releases use [stage-3-build-servicing.md](stage-3-build-servicing.md) instead.

This stage is operational, not file-editing: it pushes existing commits and queues pipelines rather than producing new commits. Because it pushes to the internal remote and can land changes irreversibly, it runs **only on explicit user instruction** and pauses for confirmation before every push, pipeline queue, and land action. It produces no commit of its own.

## Prerequisites

- Load [candidate identity, build/test and retry gates](../../references/candidate-validation.md). A green official badge is not sufficient evidence when tests failed or were disabled.

- The prepare-release playbook is complete and the working tree is clean on `stage-release-<major>.<minor>`.
- Push access to the internal release repository, and access to run its official build pipeline.

## Resolve the internal remote

The internal remote is distinct from the public GitHub remote (`dotnet`). Resolve it by its URL rather than by name -- the remote name varies between clones (`git remote -v`). It is referred to below as `<internal-remote>`.

## Sub-stage 1 - Run CI on the stage branch

Do this only when the user instructs you to push.

1. Push the stage branch to the internal remote:

   ```
   git push <internal-remote> stage-release-<major>.<minor>
   ```

2. Obtain queue approval with explicit test policy and source SHA. Run the official pipeline and check actual test/job/log results. Do not land until these gates pass or the user records a specific exception. If it fails, inspect retry safety first; fixes need fresh approved commits.

## Sub-stage 2 - Land on the internal release branch

Land only after the stage-branch pipeline is confirmed successful, and only when the user has chosen an option and confirmed. There are two options.

### Option 1 - Direct push (bypasses PR review)

Only valid once the stage-branch build/test and candidate identity gates pass (or have explicit scoped exceptions), and the user separately approves this destination push:

```
git push <internal-remote> stage-release-<major>.<minor>:internal/release/<major>.<minor>
```

### Option 2 - Internal pull request

- Create a pull request from `stage-release-<major>.<minor>` targeting `internal/release/<major>.<minor>` on the internal remote.
- Complete it with **Rebase and fast-forward** (not Squash), preserving each sub-stage commit as a linear history so it matches Option 1's direct push and keeps each sub-stage's actions auditable. Do not squash.

If normal completion requires JIT/account elevation, hand it to the human. Never recommend changing repository admin settings or protection rules to work around denied credentials.

## Sub-stage 3 - Run the official build

After the changes land on `internal/release/<major>.<minor>`, run the official build pipeline on that branch to produce the official release build whose artifacts will be published.

Obtain separate queue approval, pin the actual landed SHA and explicit `runTests` policy. Record run URL/ID/number, BAR ID, results/coverage/exclusions and artifact identities. A prior stage-branch run is not the official release build, and changed content invalidates prior candidate evidence.

## After the stage

This stage produces no repository commit. Once the official build on `internal/release/<major>.<minor>` succeeds and produces the `PackageArtifacts`, continue with **Stage 4 - Publish and Promote**.
