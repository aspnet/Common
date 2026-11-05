# Package Area Definitions

This file maps the libraries in `src/Libraries/` to logical area groups for organizing release notes. Each group name must clearly and unambiguously identify the packages it covers.

## Area groups

### AI

Packages:
- `Microsoft.Extensions.AI`
- `Microsoft.Extensions.AI.Abstractions`
- `Microsoft.Extensions.AI.OpenAI`

### AI Evaluation

Packages:
- `Microsoft.Extensions.AI.Evaluation`
- `Microsoft.Extensions.AI.Evaluation.Console`
- `Microsoft.Extensions.AI.Evaluation.NLP`
- `Microsoft.Extensions.AI.Evaluation.Quality`
- `Microsoft.Extensions.AI.Evaluation.Reporting`
- `Microsoft.Extensions.AI.Evaluation.Reporting.Azure`
- `Microsoft.Extensions.AI.Evaluation.Safety`

### Data Ingestion

Packages:
- `Microsoft.Extensions.DataIngestion`
- `Microsoft.Extensions.DataIngestion.Abstractions`
- `Microsoft.Extensions.DataIngestion.Markdig`
- `Microsoft.Extensions.DataIngestion.MarkItDown`

### Diagnostics, Health Checks, and Resource Monitoring

Packages:
- `Microsoft.Extensions.Diagnostics.ExceptionSummarization`
- `Microsoft.Extensions.Diagnostics.HealthChecks.Common`
- `Microsoft.Extensions.Diagnostics.HealthChecks.ResourceUtilization`
- `Microsoft.Extensions.Diagnostics.Probes`
- `Microsoft.Extensions.Diagnostics.ResourceMonitoring`
- `Microsoft.Extensions.Diagnostics.ResourceMonitoring.Kubernetes`
- `Microsoft.Extensions.Diagnostics.Testing`

### Compliance, Redaction, and Data Classification

Packages:
- `Microsoft.Extensions.Compliance.Abstractions`
- `Microsoft.Extensions.Compliance.Redaction`
- `Microsoft.Extensions.Compliance.Testing`

### HTTP Resilience and Diagnostics

Packages:
- `Microsoft.Extensions.Http.Resilience`
- `Microsoft.Extensions.Resilience`
- `Microsoft.Extensions.Http.Diagnostics`

### Telemetry and Observability

Packages:
- `Microsoft.Extensions.Telemetry`
- `Microsoft.Extensions.Telemetry.Abstractions`

### ASP.NET Core Extensions

Packages:
- `Microsoft.AspNetCore.Diagnostics.Middleware`
- `Microsoft.AspNetCore.HeaderParsing`
- `Microsoft.AspNetCore.Testing`
- `Microsoft.AspNetCore.AsyncState`

### Service Discovery

Packages:
- `Microsoft.Extensions.ServiceDiscovery`
- `Microsoft.Extensions.ServiceDiscovery.Abstractions`
- `Microsoft.Extensions.ServiceDiscovery.Dns`
- `Microsoft.Extensions.ServiceDiscovery.Yarp`

### Hosting, Configuration, and Ambient Metadata

Packages:
- `Microsoft.Extensions.Hosting.Testing`
- `Microsoft.Extensions.Options.Contextual`
- `Microsoft.Extensions.AmbientMetadata.Application`
- `Microsoft.Extensions.AmbientMetadata.Build`

### Caching

Packages:
- `Microsoft.Extensions.Caching.Hybrid`

### Dependency Injection and Object Pooling

Packages:
- `Microsoft.Extensions.DependencyInjection.AutoActivation`
- `Microsoft.Extensions.ObjectPool.DependencyInjection`

### Async State

Packages:
- `Microsoft.Extensions.AsyncState`

### Time Provider Testing

Packages:
- `Microsoft.Extensions.TimeProvider.Testing`

## Assigning PRs to areas

1. **Primary method — file paths**: Examine the files changed in each PR. Extract package names from paths matching `src/Libraries/{PackageName}/`. Map each package name to its area using the table above.
2. **Fallback — `area-*` labels**: If a PR has no `src/Libraries/` file changes (e.g. infrastructure PRs), check for `area-*` labels and map those to the closest area group.
3. **Multi-area PRs**: A single PR may touch multiple packages in different areas. Assign the PR to all affected areas. When writing the release notes entry, place the PR under the area most central to the change and add a brief cross-reference note for other areas if warranted.
4. **No area match**: PRs that touch only `eng/`, `scripts/`, `.github/`, `docs/`, or `test/` without corresponding `src/Libraries/` changes are infrastructure, documentation, or test PRs — categorize them by type, not by area.

## Maintaining this file

When new libraries are added to `src/Libraries/`, update this file to include them in the appropriate area group. If a new area is needed, choose a name that clearly identifies the packages it contains.
