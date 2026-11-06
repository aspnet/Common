# Build / CI-validation commands

The bump is CI-validated through the repository's Arcade build -- a real restore, build, and **pack**
of the `Microsoft.Agents.AI.ProjectTemplates` package -- followed by the template's snapshot +
execution tests. The worker's setup script (`.github/scripts/agent-framework-worker-setup.sh`) runs
this host-side before the agent and records the result in `target.json` (`validated`,
`build_summary`, `tests_summary`, with the full log tails in `/tmp/gh-aw/agent/build.log` and
`/tmp/gh-aw/agent/tests.log`).

## What it does

1. Apply the mapped `desired_versions` to `eng/packages/ProjectTemplates.props` and the aligned
   Major/Minor/Patch to the template package project.
2. Restore + build + pack the template package through the repo's Arcade build, producing the
   `.nupkg` the execution tests install:

   ```bash
   ./build.sh -ci -restore -build -pack \
     -projects "$PWD/src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.csproj" \
     -configuration Release
   ```

3. A clean pack => the package validated. Any restore/build/pack failure => `validated: false`, and
   the bump is **not** published.

## Requirements

- The repo-bootstrapped .NET SDK. `./build.sh --restore` provisions `.dotnet/dotnet` (the pinned SDK
  from `global.json`); the setup script invokes that `dotnet` directly for the test pass.
- Package restore reaches `dotnet-public` (and `nuget.org` as a transitive-dependency fallback), the
  domains allowed in the worker's `network.allowed`.
- The template's `.csproj-in` resolves its `${PackageVersion:*}` tokens from
  `eng/packages/ProjectTemplates.props` at pack time, so a routine bump edits only that props file
  and the template package project version -- never the template content.

## Agent responsibility

The agent does **not** build. It copies the already-validated
`/tmp/gh-aw/agent/ProjectTemplates.props.bumped` and
`/tmp/gh-aw/agent/ProjectTemplates.csproj.bumped` into place so what it publishes is exactly what was
validated, then performs only git + safe-output work. If `validated` is `false`, the agent must not
open or update the PR with the bump -- emit `report_incomplete` instead.
