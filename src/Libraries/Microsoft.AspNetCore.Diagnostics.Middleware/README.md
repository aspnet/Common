# Microsoft.AspNetCore.Diagnostics.Middleware

HTTP request diagnostics middleware for tracking latency and enriching and redacting log output.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.AspNetCore.Diagnostics.Middleware
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Diagnostics.Middleware" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Log Buffering

Provides a buffering mechanism for logs, allowing you to store logs in temporary circular buffers in memory. If the buffer is full, the oldest logs will be dropped. If you want to emit the buffered logs, you can call `Flush()` on the buffer. That way, if you don't flush buffers, all buffered logs will eventually be dropped and that makes sense - if you don't flush buffers, chances are
those logs are not important. At the same time, you can trigger a flush on the buffer when certain conditions are met, such as when an exception occurs.

#### Per-request Buffering

Provides HTTP request-scoped buffering for web applications:

```csharp
// Simple configuration with log level
builder.Logging.AddPerIncomingRequestBuffer(LogLevel.Warning); // Buffer Warning and lower level logs per request

// Configuration using options
builder.Logging.AddPerIncomingRequestBuffer(options =>
{
    options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Information)); // Buffer Information and lower level logs
    options.Rules.Add(new LogBufferingFilterRule(categoryName: "Microsoft.*")); // Buffer logs from Microsoft namespaces
});

// Configuration using IConfiguration
builder.Logging.AddPerIncomingRequestBuffer(configuration.GetSection("Logging:RequestBuffering"));
```

Then, to flush the buffers when a bad thing happens, call the `Flush()` method on the injected `PerRequestLogBuffer` instance:

```csharp
public class MyService
{
    private readonly PerRequestLogBuffer _perRequestLogBuffer;

    public MyService(PerRequestLogBuffer perRequestLogBuffer)
    {
        _perRequestLogBuffer = perRequestLogBuffer;
    }

    public void DoSomething()
    {
        try
        {    
            // ...
        }
        catch (Exception ex)
        {
            // Flush all buffers
            _perRequestLogBuffer.Flush();
        }
    }
}
```

Per-request buffering is especially useful for capturing all logs related to a specific HTTP request and making decisions about them collectively based on request outcomes.
Per-request buffering is tightly coupled with [Global Buffering](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Telemetry/README.md#log-buffering). If a log entry is supposed to be buffered to a per-request buffer, but there is no active HTTP context, it will be buffered to the global buffer instead. If buffer flush is triggered, the per-request buffer will be flushed first, followed by the global buffer.

To preserve logging scopes in buffered log records, set the experimental `IncludeScopes` option, which adds the scopes' name/value pairs to the records' attributes.

### Tracking HTTP Request Latency

These components enable tracking and reporting the latency of HTTP request processing.

The services can be registered using the following methods. They also register the latency context services (see `AddLatencyContext()`) unless those are already registered:

```csharp
public static IServiceCollection AddRequestCheckpoint(this IServiceCollection services)
public static IServiceCollection AddRequestLatencyTelemetry(this IServiceCollection services)
public static IServiceCollection AddRequestLatencyTelemetry(this IServiceCollection services, Action<RequestLatencyTelemetryOptions> configure)
public static IServiceCollection AddRequestLatencyTelemetry(this IServiceCollection services, IConfigurationSection section)
```

The middleware can be registered using the following methods:

```csharp
public static IApplicationBuilder UseRequestCheckpoint(this IApplicationBuilder builder)
public static IApplicationBuilder UseRequestLatencyTelemetry(this IApplicationBuilder builder)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRequestLatencyTelemetry();
builder.Services.AddRequestCheckpoint();

var app = builder.Build();

app.UseRequestCheckpoint();
app.UseRequestLatencyTelemetry();
```

#### Enriching HTTP Request Logs with Latency Data

This enricher appends the latency data collected for an incoming request (checkpoints, tags, measures, and total duration) to that request's HTTP log. It reads the per-request latency context populated by `AddRequestLatencyTelemetry`, so register the latency telemetry services and middleware as shown above.

This API is only available for ASP.NET Core 8+.

The enricher can be registered using the following method:

```csharp
public static IServiceCollection AddHttpServerLatencyTelemetry(this IServiceCollection services)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRequestLatencyTelemetry();
builder.Services.AddRequestCheckpoint();
builder.Services.AddHttpLogging();
builder.Services.AddHttpServerLatencyTelemetry();

var app = builder.Build();

app.UseRequestCheckpoint();
app.UseHttpLogging();
app.UseRequestLatencyTelemetry();
```

### HTTP Request Logs Enrichment and Redaction

These components enable enriching and redacting ASP.NET Core's [HTTP request logs](https://learn.microsoft.com/aspnet/core/fundamentals/http-logging/).

These APIs are only available for ASP.NET Core 8+.

`AddHttpLoggingRedaction()` registers a default redactor provider by calling `AddRedaction()` from the `Microsoft.Extensions.Compliance.Redaction` package. `AddHttpLogEnricher<T>()` and `AddHttpServerLatencyTelemetry()` call `AddHttpLoggingRedaction()`, so they register it too. The default redactor provider erases any data that has a data classification other than `DataClassification.None`. To use specific redactors for your data classifications, call `builder.Services.AddRedaction(...)` and configure them; this can be done before or after calling any of these methods. If you use `AddFakeRedaction()` or a custom `IRedactorProvider` implementation, it must be registered before calling any of these methods.

The services can be registered using the following methods:

```csharp
public static IServiceCollection AddHttpLoggingRedaction(this IServiceCollection services, Action<HeaderParsingOptions>? configure = null)
public static IServiceCollection AddHttpLoggingRedaction(this IServiceCollection services, IConfigurationSection section)
public static IServiceCollection AddHttpLogEnricher<T>(this IServiceCollection services)
```

The middleware can be registered using the following method:

```csharp
public static IApplicationBuilder UseHttpLogging(this IApplicationBuilder builder)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

// General logging options
builder.Services.AddHttpLogging(options => { });
// Redaction options
builder.Services.AddHttpLoggingRedaction(options => { });

var app = builder.Build();

app.UseHttpLogging();
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
