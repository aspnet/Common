---
name: update-agent-framework-template
description: >-
  Keep the aiagent-webapi project template's Microsoft.Agents.AI package versions aligned with the
  newest coherent Microsoft Agent Framework release published on the dnceng "dotnet-public" NuGet
  feed. Use when asked to "update the Agent Framework template", "bump the aiagent-webapi template",
  "align the project template with the latest Agent Framework", or when given an Agent Framework
  release signal from agent-framework-discover.cs. Covers how to detect the release, map it onto the
  template's package subset, CI-validate the bump, and format the maintained draft pull request.
agent: 'agent'
tools: ['github/*', 'bash']
---

# Update Agent Framework Template

Keep the `aiagent-webapi` project template shipped by `Microsoft.Agents.AI.ProjectTemplates` aligned
with the newest coherent **Microsoft Agent Framework** release. The template's package versions are
the single source of truth in `eng/packages/ProjectTemplates.props`; this skill is the authority for
**how** to detect the release, map it onto the template's packages, validate the bump, and format the
pull request. The `agent-framework-worker` workflow owns the **lifecycle** (which PR to touch and
what state to leave it in).

## The release signal

`.github/scripts/agent-framework-discover.cs` (a file-based C# app using the NuGet client SDK) is the
authoritative release detector. It reads the `dotnet-public` feed and emits, for the whole
`Microsoft.Agents.AI*` family, the newest coherent release: a `release_version` (the anchor
`Microsoft.Agents.AI` newest stable, e.g. `1.13.0`), a `release_date` (the shared `YYMMDD` stamp), and
per-package `at_release` versions. See [references/version-detection.md](references/version-detection.md).

Drive the signal **only** off `dotnet-public` -- it is the feed the template's package restore
validates against, so a version detected there is guaranteed restorable by CI. Never hand-parse the
feed's flat-container JSON: the NuGet client SDK's `NuGetVersion`/`VersionComparer` gives correct
SemVer ordering, sidestepping the feed's descending sort order and the anomalous `0.0.1-preview.*`
entry.

## Bumping the packages (in lockstep)

The Agent Framework packages ship **as a set** for each release, so bump **every**
`Microsoft.Agents.AI*` package pinned in `eng/packages/ProjectTemplates.props` together -- do not bump
a subset. Discover the package ids from the props file itself (data-driven, so nothing is missed as
the family grows), and for each set its version to that package's `at_release` value from the signal:

```
desired[id] = signal.packages[id].at_release   for each Microsoft.Agents.AI* id in the props
```

**Exclude `Microsoft.Agents.AI.ProjectTemplates`** -- the template package itself is on its own
cadence and is not part of the released set.

Each package keeps its **own stabilization tier** -- do not force all packages to a stable version.
At the current release, `Microsoft.Agents.AI` / `.Abstractions` / `.OpenAI` / `.Workflows` /
`.Workflows.Generators` are `stable`, `.DevUI` / `.Hosting` / `.Foundry` / `.Foundry.Hosting` are
`-preview.*`, and `.Hosting.OpenAI` is `-alpha.*`. Tiers can change between releases (e.g. a package
that was stable can move to preview-only), which is exactly why each package's version comes from its
own `at_release` rather than a shared value.

## What to change

A routine release bump touches two files:

1. `eng/packages/ProjectTemplates.props` -- update the `Version` attribute of every
   `Microsoft.Agents.AI*` `<PackageVersion>` item (except `Microsoft.Agents.AI.ProjectTemplates`) to
   its mapped `at_release` version.
2. `src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.csproj`
   -- align the template NuGet package's own version with the release by setting `<MajorVersion>`,
   `<MinorVersion>`, and `<PatchVersion>` to the release's Major.Minor.Patch. **Never** change
   `<PreReleaseVersionLabel>` -- the prerelease label portion is left as-is (e.g. `1.3.0-preview` ->
   `1.13.0-preview`).

See [references/change-classification.md](references/change-classification.md) for when more is needed
(a newly split package tier, an added/removed package, or a framework API change that would require
editing the template's own source or other consumption).

## Validate before publishing

Every bump must be CI-validated before it is published. The worker's setup script does this host-side
and records the results in `target.json`: it restores, builds, and packs the
`Microsoft.Agents.AI.ProjectTemplates` package through the repo's Arcade build, then runs the
template's snapshot + execution tests. Never open or update a PR with an unvalidated
(`validated: false`) bump. Evaluating whether other Agent Framework consumption under
`src/Libraries/Microsoft.Extensions.AI*` needs updates is the **agent's** job (see below), not the
host build. See
[references/build-commands.md](references/build-commands.md) and
[references/testing.md](references/testing.md).

## Evaluate changes across the release

Beyond bumping versions, evaluate what changed in Agent Framework between the previously integrated
version and the target release, and bring the template and any other consumption up to the currently
prescribed patterns. The setup script gathers the `microsoft/agent-framework` `dotnet-*` release
notes for the range into `af-changes.md` for you. See
[references/evaluate-changes.md](references/evaluate-changes.md).

## Pull request format

The maintained PR is a single continuously-updated **draft** against `main`, labeled `automation` +
`area-ai-templates`, on the `update-agent-framework-template` branch, carrying a machine-readable
tracking block. See [references/pr-description.md](references/pr-description.md).
