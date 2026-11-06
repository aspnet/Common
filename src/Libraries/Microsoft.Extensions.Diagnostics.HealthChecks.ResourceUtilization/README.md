# Microsoft.Extensions.Diagnostics.HealthChecks.ResourceUtilization

This provides configurable health check reporting based on the current system resource utilization. See `Microsoft.Extensions.Diagnostics.ResourceMonitoring` for details on how resources are measured. See the [health checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) documentation for general usage guidance.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.ResourceUtilization
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.ResourceUtilization" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

The health check services can be registered and configured using any of the following API:

```csharp
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, params string[] tags)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, IEnumerable<string> tags)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, IConfigurationSection section)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, IConfigurationSection section, params string[] tags)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, IConfigurationSection section, IEnumerable<string> tags)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, Action<ResourceUtilizationHealthCheckOptions> configure)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, Action<ResourceUtilizationHealthCheckOptions> configure, params string[] tags)
public static IHealthChecksBuilder AddResourceUtilizationHealthCheck(this IHealthChecksBuilder builder, Action<ResourceUtilizationHealthCheckOptions> configure, IEnumerable<string> tags)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddResourceUtilizationHealthCheck(o =>
    {
        o.CpuThresholds = new ResourceUsageThresholds
        {
            DegradedUtilizationPercentage = 80,
            UnhealthyUtilizationPercentage = 90,
        };
        o.MemoryThresholds = new ResourceUsageThresholds
        {
            DegradedUtilizationPercentage = 80,
            UnhealthyUtilizationPercentage = 90,
        };
        o.SamplingWindow = TimeSpan.FromSeconds(5);
    });

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();
```

`CpuThresholds` and `MemoryThresholds`'s percentages default to `null` and will not be reported as degraded or unhealthy unless configured. The `SamplingWindow` defaults to 5 seconds.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
