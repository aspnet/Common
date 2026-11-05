# Historical Releases

Mapping of OpenTelemetry GenAI semantic-conventions releases to dotnet/extensions PRs.

> **Migration Note**: Every entry in the tables below is from the consolidated [`open-telemetry/semantic-conventions`](https://github.com/open-telemetry/semantic-conventions) repo, where GenAI conventions were previously managed and tagged with `area:gen-ai`. GenAI conventions are now maintained in a dedicated repo: [`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai). See [SKILL.md §Migration Note](../SKILL.md#migration-note) for the full picture.
>
> **Point-in-time reference**: This file is not intended to be kept up to date with every new release. It provides context for how past convention updates were handled. For current activity, consult:
>
> - The [`semantic-conventions-genai` `changelog.d/` fragments](https://github.com/open-telemetry/semantic-conventions-genai/tree/main/changelog.d) (Towncrier news fragments are the live "what's new" view until releases land; the `CHANGELOG.md` `Unreleased` section stays empty until release).
> - The [`semantic-conventions-genai` releases page](https://github.com/open-telemetry/semantic-conventions-genai/releases) (empty as of this writing).
> - The [`semantic-conventions-genai` pull request history](https://github.com/open-telemetry/semantic-conventions-genai/pulls?q=is%3Apr).
> - The dotnet/extensions PR history.

## Release History

### Pre-v1.31 (Pre-release Era)

| Convention Version | dotnet/extensions PR | Description |
|-------------------|---------------------|-------------|
| Initial implementation | [#5532](https://github.com/dotnet/extensions/pull/5532) | Initial OpenTelemetry gen-ai instrumentation |
| v1.29 draft | [#5712](https://github.com/dotnet/extensions/pull/5712) | Align with v1.29 draft conventions |
| v1.30 | [#5815](https://github.com/dotnet/extensions/pull/5815) | Update to v1.30 conventions |

### v1.31–v1.40 (Stable Release Era)

| Convention Version | dotnet/extensions PR | Description |
|-------------------|---------------------|-------------|
| v1.31 | [#6073](https://github.com/dotnet/extensions/pull/6073) | Update to v1.31 conventions |
| v1.34 | [#6466](https://github.com/dotnet/extensions/pull/6466) | Update to v1.34 conventions |
| v1.35 | [#6557](https://github.com/dotnet/extensions/pull/6557) | Update to v1.35 conventions |
| v1.36 | [#6579](https://github.com/dotnet/extensions/pull/6579) | Bump version reference to v1.36 (CCA) |
| v1.37 | [#6767](https://github.com/dotnet/extensions/pull/6767) | Update to v1.37 conventions |
| v1.38 | [#6829](https://github.com/dotnet/extensions/pull/6829) | Update to v1.38 conventions |
| v1.38 update | [#6981](https://github.com/dotnet/extensions/pull/6981) | Additional v1.38 changes |
| v1.39 | [#7274](https://github.com/dotnet/extensions/pull/7274) | Bump version reference to v1.39 (CCA) |
| v1.40 | [#7322](https://github.com/dotnet/extensions/pull/7322) | Update to v1.40 conventions (CCA audit) |

### Feature-Specific PRs (v1.38–v1.40 era)

These PRs implemented specific gen-ai convention features rather than being tied to a single version bump:

| PR | Feature | Convention Source |
|----|---------|-------------------|
| [#7240](https://github.com/dotnet/extensions/pull/7240) | Server-side tool call attributes | v1.37+ |
| [#7241](https://github.com/dotnet/extensions/pull/7241) | Metric computation fix | Bug fix |
| [#7325](https://github.com/dotnet/extensions/pull/7325) | Streaming metrics (time_to_first_chunk, time_per_output_chunk) | v1.39 |
| [#7379](https://github.com/dotnet/extensions/pull/7379) | Exception event recording (gen_ai.client.operation.exception) | v1.40 |
| [#7382](https://github.com/dotnet/extensions/pull/7382) | invoke_workflow operation name | v1.40 |

## Typical Change Patterns by Release

### Version-only releases (v1.36, v1.39)
- Only doc comment version bump needed
- Minimal code changes
- Quick turnaround

### Attribute addition releases (v1.31, v1.34, v1.35, v1.37, v1.38)
- New constants in `OpenTelemetryConsts.cs`
- New attribute emission in one or more OpenTelemetry* clients
- Test updates
- Version bump

### Behavioral change releases (v1.40 features)
- New code patterns (exception recording, streaming metrics)
- May require shared infrastructure (`Common/` classes)
- More extensive test changes
- Often split into multiple PRs per feature
