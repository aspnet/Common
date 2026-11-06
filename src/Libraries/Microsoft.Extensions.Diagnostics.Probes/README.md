# Microsoft.Extensions.Diagnostics.Probes

Answers Kubernetes liveness, startup, and readiness TCP probes based on the results from the [Health Checks service](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks).

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Diagnostics.Probes
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.Probes" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

The health check endpoints can be registered and configured with the following methods:

```csharp
public static IServiceCollection AddKubernetesProbes(this IServiceCollection services)
public static IServiceCollection AddKubernetesProbes(this IServiceCollection services, IConfigurationSection section)
public static IServiceCollection AddKubernetesProbes(this IServiceCollection services, Action<KubernetesProbesOptions> configure)
```

Each type of probe handler can have its details configured separately.

```csharp
services.AddKubernetesProbes(options =>
{
    options.LivenessProbe.TcpPort = 2305;
    options.StartupProbe.TcpPort = 2306;
    options.ReadinessProbe.TcpPort = 2307;
})
```

The `HealthAssessmentPeriod` property defines how often the health-checks are assessed. By default 30 seconds.

Each probe can also specify `Func<HealthCheckRegistration, bool>? FilterChecks` to customize which health checks are run.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
