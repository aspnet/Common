# Change classification

Classify the delta between the newest release and what `eng/packages/ProjectTemplates.props`
currently pins, and change only what each class requires.

## 🟢 Routine version bump (the common case)

Every referenced package's major.minor.patch moved, but the set of packages and the template's own
source are unchanged. Change two things:

1. `eng/packages/ProjectTemplates.props` -- update the `Version` attribute of each
   `Microsoft.Agents.AI*` `<PackageVersion>` item to its mapped `at_release` version.
2. The template package project version -- align `<MajorVersion>`/`<MinorVersion>`/`<PatchVersion>`
   in `src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.csproj`
   to the release, leaving `<PreReleaseVersionLabel>` unchanged.

Nothing else needs to change; the template's `${PackageVersion:*}` tokens resolve from the props at
pack time, so the package packs and its snapshot + execution tests pass against the new versions.

## 🟡 Structural change (occasional)

A package's tier changed (e.g. a package that used to reuse the core token now trails on its own
version), or a package was added to / removed from the Agent Framework. In addition to the version
bump, update the template's `<PackageReference>` list -- in the shipped template `.csproj-in` under
`src/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates/templates/AIAgentWebApi-CSharp/` -- to
add/remove the reference or point it at the right `${PackageVersion:*}` key. Because the template is a
`.csproj-in` (not a directly buildable project), changes to it are validated through the snapshot +
execution tests, which regenerate/compile the generated project.

## 🔴 Framework API change (rare)

The new Agent Framework version changed an API the template's source uses, so the generated project no
longer compiles. The CI validation (see build-commands.md) is what catches this: the execution tests
fail and `validated` will be `false`. Do **not** publish an unvalidated bump. A framework API change
requires editing the template's own source (`Program.cs` etc.) to match the new API and regenerating
the affected `.verified` snapshots -- treat it as a real change for human review rather than an
automatic version bump, and surface the failure in the PR body.

## Not in scope

Non-Agent-Framework packages in the props file (e.g. `OpenTelemetry.Api`) are outside this
automation's routine bump. Leave them unless review feedback explicitly asks otherwise and the change
stays within the allowed files and CI-validates.
