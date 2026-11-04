# Validate Release

Confirms a published `dotnet/extensions` release is correct and finalizes it: verify every published
package's exact-version dependencies resolve, verify that published symbols are on the Microsoft
symbol server (msdl) with working Source Link, then reconcile the internal, public, and `main`
branches.

Run this playbook after **publish-release**.

Load/present [the canonical workflow and checkpoint](../references/workflow.md). Prepublication original PDB metadata is Stage 4a; public URL validation must wait for source reachability, so Stage 6a can precede Stage 5b without pretending Stage 5 passed.

- Stage 5 verifies package dependency resolution and symbols.
- Stage 6 stages reconciliation merges when needed (pushing and PR completion are left to the user).
- Stage 7 handles support-page follow-up based on release type and package novelty.

For servicing releases prepared directly on public `release/<major>.<minor>`, Stage 6 is often unnecessary because commits were backported from `main` into the release branch up front. In that case, run Stage 5, follow the Stage 7 decision gate (skip if applicable), and run Stage 6 only if the user explicitly asks for additional branch-flow follow-up.

## Stage 5 - Verify Package Dependencies, Source Link, and Symbols

Verify clean consumer dependency resolution and every library TFM's matching msdl PDB. Then validate full Source Link source URLs/checksums after source publication. These are sign-off gates, with explicit exceptions recorded as exceptions, not passes. Missing symbols may reflect transport, identity, indexing or channel issues; they do not prove a promotion failed. Bound retries and diagnose the evidence.

Read and follow [references/stage-5-verify-source-link.md](references/stage-5-verify-source-link.md).

## Stage 6 - Reconcile Branches

Merge the internal release branch back out to the public `release/<major>.<minor>` branch (discarding the internal-only infrastructure changes), then merge that public branch into `main` (reverting the stable-versioning flip). Each merge lands as a merge commit (never squashed); the agent stages the merges and shows the diff, but pushing and completing the merge-commit PRs (which need elevation) are user-directed.

Read and follow [references/stage-6-reconcile-branches.md](references/stage-6-reconcile-branches.md).

## Stage 7 - Confirm the Support-Page Update

Stage 7 is conditional:

- **Monthly releases**: the support page is maintained by a partner team; review their PR and confirm completeness.
- **Servicing releases that only service existing packages**: Stage 7 can be skipped.
- **Servicing releases that introduce newly shipped packages**: the release manager must author and submit the `dotnet/website` PR that adds those packages to the support page.

Read and follow [references/stage-7-support-page.md](references/stage-7-support-page.md).

## Done

Close out only after required work is completed or has explicit recorded exceptions; list those exceptions instead of claiming full validation. Tagging and publishing GitHub release notes use the **write-release-notes** playbook's build-source/nuspec provenance gate.
