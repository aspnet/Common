# Template snapshot and integration tests

The `aiagent-webapi` template has snapshot + integration tests under
`test/ProjectTemplates/Microsoft.Agents.AI.ProjectTemplates.IntegrationTests`. They must pass for
every published bump. The worker's setup script runs them host-side and records the result in
`target.json.tests_summary`.

## Snapshot tests

Each generated template file is compared, scrubbed to be version-agnostic (package versions become
`{VERSION}`), against a committed `.verified` snapshot under `Snapshots/aiagent-webapi/`. This means:

- A **pure version bump** does not change the snapshots (versions are scrubbed) -- they keep passing.
- A change to the template's **content** (e.g. `Program.cs`, or adding/removing a `<PackageReference>`
  in the template project) does change the generated output, so the affected `.verified` snapshot
  must be **regenerated** to match the intended new content, or the snapshot test fails.

Regenerate a snapshot by copying the current (scrubbed) template file over its `.verified` counterpart
once the new content is intentional, then re-run the tests.

## Integration (execution) tests

The execution test packs the template, runs `dotnet new aiagent-webapi` in a sandbox, then restores
and builds the generated project against the Agent Framework versions currently pinned in
`eng/packages/ProjectTemplates.props`. It is the canary for a version that does not restore, or a
template that does not compile against the new Agent Framework surface.

## Never publish red

If the tests fail and you cannot make them pass within the allowed files (including regenerating
snapshots for intended content changes), do **not** open or update the PR with the change -- emit
`report_incomplete` explaining why, so the failure is surfaced instead of shipped.
