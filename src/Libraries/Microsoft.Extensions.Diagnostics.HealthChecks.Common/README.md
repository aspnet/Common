# Microsoft.Extensions.Diagnostics.HealthChecks.Common

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

This package contains common health check implementations. Here are the main features it provides:

Application Lifecycle Health Check
- A provider that's tied to the application's lifecycle based on `IHostApplicationLifetime`
- Emits unhealthy status when the application is not started, stopping, or stopped
- Emits healthy status when the application is started and running

Manual Health Check
- A provider that enables manual control of the application's health
- Emits unhealthy status when `ReportUnhealthy` is called
- Emits healthy status when `ReportHealthy` is called

Telemetry Publisher
- A publisher which emits telemetry (logs and counters) representing the application's health
- Can be configured to log only when unhealthy reports are received

See the [health checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) documentation for general usage guidance.

See the [built in metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-diagnostics#meter-microsoftextensionsdiagnosticshealthchecks) page for information regarding the metrics emitted by the telemetry publisher.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.Common
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Common" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Application Lifecycle Health Check

The application's lifecycle health check service can be registered and configured using any of the following API:

```csharp
public static IHealthChecksBuilder AddApplicationLifecycleHealthCheck(this IHealthChecksBuilder builder, params string[] tags)
public static IHealthChecksBuilder AddApplicationLifecycleHealthCheck(this IHealthChecksBuilder builder, IEnumerable<string> tags)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddApplicationLifecycleHealthCheck();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();
```

### Manual Health Check

The manual health check service can be registered and configured using any of the following API:

```csharp
public static IHealthChecksBuilder AddManualHealthCheck(this IHealthChecksBuilder builder, params string[] tags)
public static IHealthChecksBuilder AddManualHealthCheck(this IHealthChecksBuilder builder, IEnumerable<string> tags)
```

Then you can inject `IManualHealthCheck<>` into your services and call the following methods:

```csharp
public static void ReportHealthy(this IManualHealthCheck manualHealthCheck)
public static void ReportUnhealthy(this IManualHealthCheck manualHealthCheck, string reason)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddManualHealthCheck();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();

public class MyService
{
    private readonly IManualHealthCheck<MyService> healthCheck;

    // inject IManualHealthCheck<> into your service
    public MyService(IManualHealthCheck<MyService> healthCheck)
    {
        this.healthCheck = healthCheck;
    }

    public void DoSomething()
    {
        // ... do something ...

        if (somethingBadHappened)
        {
            this.healthCheck.ReportUnhealthy("reason");
        }

        this.healthCheck.ReportHealthy();
    }
}
```

### Telemetry Publisher

The telemetry publisher can be registered and configured using any of the following API:

```csharp
public static IServiceCollection AddTelemetryHealthCheckPublisher(this IServiceCollection services)
public static IServiceCollection AddTelemetryHealthCheckPublisher(this IServiceCollection services, IConfigurationSection section)
public static IServiceCollection AddTelemetryHealthCheckPublisher(this IServiceCollection services, Action<TelemetryHealthCheckPublisherOptions> configure)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

// register health check services as needed
builder.Services.AddHealthChecks()
    .AddCheck<SampleHealthCheck>("Sample");

// register telemetry publisher
builder.Services.AddTelemetryHealthCheckPublisher();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
