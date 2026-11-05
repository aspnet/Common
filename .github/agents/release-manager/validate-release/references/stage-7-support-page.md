# Stage 7 - Support-Page Follow-up

The [.NET Platform Extensions support policy page](https://dotnet.microsoft.com/en-us/platform/support/policy/extensions)
lists supported packages and current versions. This stage differs by release type and package novelty.

## Decision gate

Use [the canonical checkpoint](../../references/workflow.md). For this one-file website task, prefer an existing checkout or bounded shallow/partial checkout/worktree; do not pay for a full clone unnecessarily. Do not create a long-lived project in ephemeral session storage without an explicit retention plan.

1. Determine whether the release is **monthly** or **servicing**.
2. Determine whether the shipped scope includes any **newly published packages**.

Then follow one path:

- **Monthly release** -> review partner-team PR (Path A).
- **Servicing release, existing packages only** -> skip Stage 7 (Path B).
- **Servicing release with new package(s)** -> release manager authors `dotnet/website` PR (Path C).

## Path A - Monthly release (review partner-team PR)

The support page is maintained by a partner team in the monthly flow; this path is a review.

## Steps

1. **Find the pull request** into [`dotnet/website`](https://github.com/dotnet/website) that updates the extensions support page for this release (it edits `website/src/netlandingpage/Pages/platform/support/policy/extensions.cshtml`). Search open and recently-merged PRs.
2. **Review it** for this release:
   - The supported version and its release date are updated to this release.
   - The previous minor version has moved to the out-of-support table with the correct end-of-support date.
3. **Check the package list** against what shipped. Compare the page's listed packages to the packages published in this release, and flag any that are missing -- in particular:
   - **Newly published** packages (new in this release).
   - **Recently-stabilized** packages (previously preview, now stable as of this release).

   Exclude preview-only packages, which are not listed.
4. If the pull request is missing or incomplete, raise it with the partner team.

Treat monthly version/date rollover and missing stable package entries as separate review scopes; confirm whether the user wants a separate entries PR. A missing stable entry may be an older omission, not a newly introduced package. Exclude preview-only entries by page policy. Use prior PR **diffs**, not titles, to distinguish monthly rollover from patch updates.

## Path B - Servicing release with existing packages only

No support-page PR is required. Record that Stage 7 is intentionally skipped because the servicing
release did not introduce new package entries.

## Path C - Servicing release with newly published package(s)

In servicing flow, the release manager owns the website update when a new package is introduced.

1. Create a PR in [`dotnet/website`](https://github.com/dotnet/website) updating
   `website/src/netlandingpage/Pages/platform/support/policy/extensions.cshtml`.
2. Update:
   - supported version and release date for this release,
   - package list entries for newly published packages,
   - any required out-of-support table changes.
3. Ensure preview-only packages are not listed.
4. Submit the PR and track it to completion as part of release sign-off.

For either authoring path, recheck open **and recently merged** partner PRs by changed file/content immediately before push/PR creation; an earlier title-only sweep is insufficient. If the user chooses an overlapping partner PR, abandon the duplicate work without pushing it. Keep package publication date, GitHub release date, page "Last updated" edit date, PR merge and deployment separate; confirm intended date semantics and preserve unrelated page history. A merged PR is not proof of deployment; record website reachability/deployed content separately.

## After the stage

This is the final validation step when applicable. The release is complete once symbols are public
(Stage 5), branch reconciliation is complete when required (Stage 6), and Stage 7 obligations for the
selected path are satisfied.
