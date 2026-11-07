# Microsoft.Extensions.Telemetry

This library provides advanced logging and telemetry enrichment capabilities for .NET applications. It allows for detailed and configurable enrichment of log entries, along with enhanced latency monitoring and logging features. It is built for applications needing sophisticated telemetry and logging insights.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Telemetry
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Telemetry" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage

### Log Sampling

The library provides two types of log sampling mechanisms: **Random Probabilistic Sampling** and **Trace-based Sampling**.

#### Random Probabilistic Sampling

Provides configurable probability-based sampling with flexible rules:

```csharp
// Simple configuration with probability
builder.Logging.AddRandomProbabilisticSampler(0.1); // Sample 10% of all logs, meaning - 90% of logs will be dropped
builder.Logging.AddRandomProbabilisticSampler(0.1, LogLevel.Warning); // Sample 10% of Warning and lower level logs

// Configuration using options
builder.Logging.AddRandomProbabilisticSampler(options =>
{
    options.Rules.Add(new RandomProbabilisticSamplerFilterRule(0.1, logLevel: LogLevel.Information)); // Sample 10% of Information and lower level logs
    options.Rules.Add(new RandomProbabilisticSamplerFilterRule(1.0, logLevel: LogLevel.Error)); // Sample all Error logs
});

// Configuration using IConfiguration
builder.Logging.AddRandomProbabilisticSampler(configuration.GetSection("Logging:Sampling"));
```

The Random Probabilistic Sampler supports the `IOptionsMonitor<T>` pattern, allowing for dynamic configuration updates. This means you can change the sampling rules at runtime without needing to restart your application.

#### Trace-Based Sampling

Matches logging sampling decisions with the underlying [Distributed Tracing sampling decisions](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing-concepts#sampling):

```csharp
// Add trace-based sampler
builder.Logging.AddTraceBasedSampler();
```

This comes in handy when you already use OpenTelemetry .NET Tracing and would like to see sampling decisions being consistent across both logs and their underlying [`Activity`](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing-concepts#sampling).

### Log Buffering

Provides a buffering mechanism for logs, allowing you to store logs in temporary circular buffers in memory. If the buffer is full, the oldest logs will be dropped. If you want to emit the buffered logs, you can call `Flush()` on the buffer. That way, if you don't flush buffers, all buffered logs will eventually be dropped and that makes sense - if you don't flush buffers, chances are
those logs are not important. At the same time, you can trigger a flush on the buffer when certain conditions are met, such as when an exception occurs.

This library works with all logger providers, even if they do not implement the `Microsoft.Extensions.Logging.Abstractions.IBufferedLogger` interface. In that case, the library will
be calling `ILogger.Log()` method directly on every single buffered log record when flushing the buffer.

#### Global Buffering

Provides application-wide log buffering with configurable rules:

```csharp
// Simple configuration with log level
builder.Logging.AddGlobalBuffer(LogLevel.Warning); // Buffer Warning and lower level logs

// Configuration using options
builder.Logging.AddGlobalBuffer(options =>
{
    options.Rules.Add(new LogBufferingFilterRule(logLevel: LogLevel.Information)); // Buffer Information and lower level logs
    options.Rules.Add(new LogBufferingFilterRule(categoryName: "Microsoft.*")); // Buffer logs from Microsoft namespaces
});

// Configuration using IConfiguration
builder.Logging.AddGlobalBuffer(configuration.GetSection("Logging:Buffering"));
```

Then, to flush the global buffer when a bad thing happens, call the `Flush()` method on the injected GlobalLogBuffer instance:

```csharp
public class MyService
{
    private readonly GlobalLogBuffer _globalLogBuffer;

    public MyService(GlobalLogBuffer globalLogBuffer)
    {
        _globalLogBuffer = globalLogBuffer;
    }

    public void DoSomething()
    {
        try
        {    
            // ...
        }
        catch (Exception ex)
        {
            // Flush the global buffer when an exception occurs
            _globalLogBuffer.Flush();
        }
    }
}
```

The Global Log Buffer supports the `IOptionsMonitor<T>` pattern, allowing for dynamic configuration updates. This means you can change the buffering rules at runtime without needing to restart your application.

To preserve logging scopes in buffered log records, set the experimental `IncludeScopes` option, which adds the scopes' name/value pairs to the records' attributes.

#### Limitations

1. This library does not preserve the order of log records. However, original timestamps are preserved.
1. The library does not support custom configuration per each logger provider. Same configuration is applied to all logger providers.
1. Log scopes are only preserved if the `IncludeScopes` option is enabled, and then only as attributes of buffered log records.
1. When buffering and then flushing buffers, not all information of the original log record is preserved. This is due to serializing/deserializing limitation, but can be
revisited in future. Namely, values of log record attributes are converted to strings, and exceptions are preserved as their messages only.
1. The following `Microsoft.Extensions.Logging.Abstractions.BufferedLogRecord` properties are available only to loggers implementing the `Microsoft.Extensions.Logging.Abstractions.IBufferedLogger` interface:

- `Microsoft.Extensions.Logging.Abstractions.BufferedLogRecord.ActivitySpanId`
- `Microsoft.Extensions.Logging.Abstractions.BufferedLogRecord.ActivityTraceId`
- `Microsoft.Extensions.Logging.Abstractions.BufferedLogRecord.ManagedThreadId`
- `Microsoft.Extensions.Logging.Abstractions.BufferedLogRecord.MessageTemplate`

### Application Log Enrichment

Enriches logs with application-specific information based on `ApplicationMetadata` information. The bellow calls will add the application log enricher to the service collection.

```csharp
// Add service log enricher with default settings
builder.Services.AddApplicationLogEnricher();

// Or configure with options
builder.Services.AddApplicationLogEnricher(options =>
{
    options.ApplicationName = true;
    options.BuildVersion = true;
    options.DeploymentRing = true;
    options.EnvironmentName = true;
});
```

### Latency Monitoring

Provides tools for latency data collection and export. The bellow example uses the built-in Console exporter, but custom exporters can be created by implementing the `ILatencyDataExporter` interface.

```csharp
// Add latency console data exporter with configuration
builder.Services.AddConsoleLatencyDataExporter(options =>
{
    options.OutputCheckpoints = true;
    options.OutputMeasures = true;
    options.OutputTags = true;
});
```

In order for the latency data to be exported, a call to `ILatencyDataExporter.ExportAsync()` is required. This can either be called manually, or by using the Request Latency Middleware inside the `Microsoft.AspNetCore.Diagnostics.Middleware` package by adding:

```csharp
// Add Latency Context
builder.Services.AddLatencyContext();

// Add Checkpoints, Measures, Tags
builder.Services.RegisterCheckpointNames("databaseQuery", "externalApiCall");
builder.Services.RegisterMeasureNames("responseTime", "processingTime");
builder.Services.RegisterTagNames("userId", "transactionId");

// Add Console Latency exporter.
builder.Services.AddConsoleLatencyDataExporter();
// Optionally add custom exporters.
builder.Services.AddSingleton<ILatencyDataExporter, MyCustomExporter>();

// Add Request latency telemetry.
builder.Services.AddRequestLatencyTelemetry();

// ...

// Add Request Latency Middleware which will automatically call ExportAsync on all registered latency exporters.
app.UseRequestLatencyTelemetry();
```

### Logging Enhancements

Offers additional logging capabilities like stack trace capturing, exception message inclusion, and log redaction.

```csharp
// Enable log enrichment.
builder.Logging.EnableEnrichment(options =>
{
    options.CaptureStackTraces = true;
    options.IncludeExceptionMessage = true;
    options.MaxStackTraceLength = 500;
    options.UseFileInfoForStackTraces = true;
});

builder.Services.AddApplicationLogEnricher(); // <- This call is required in order for the enricher to be added into the service collection.

// Enable log redaction
builder.Logging.EnableRedaction(options =>
{
    options.ApplyDiscriminator = true;
});

builder.Services.AddRedaction(); // <- This call is required in order for the redactor provider to be added into the service collection.

```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
