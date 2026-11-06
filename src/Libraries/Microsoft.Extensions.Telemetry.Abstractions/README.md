# Microsoft.Extensions.Telemetry.Abstractions

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

This package contains common abstractions for high-level telemetry primitives. Here are the main features it provides:

- Enhanced Logging Capabilities
- Log Enrichment
- Latency Measurement
- HTTP Request Metadata Handling

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Telemetry.Abstractions
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Telemetry.Abstractions" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage

### Enhanced Logging Capabilities

The package includes a custom logging generator that enhances the default .NET logging capabilities by replacing the default generator. This generator automatically logs the contents of collections and offers advanced logging features, significantly improving the debugging and monitoring process.

```csharp
[LoggerMessage(1, LogLevel.Information, "These are the contents of my dictionary: {temperature}")]
internal static partial void LogMyDictionary(ILogger<Program> logger, Dictionary<int, string> temperature);
```

It also adds the `LogProperties` attribute which can be applied to an object parameter of a `LoggerMessage` method. It introspects the passed-in object and automatically adds tags for all its properties. This leads to more informative logs without the need for manual tagging of each property.

```csharp
[LoggerMessage(1, LogLevel.Information, "Detected a new temperature: {temperature}")]
internal static partial void LogNewTemperature(ILogger<Program> logger, [LogProperties] Temperature temperature);

internal record Temperature(double value, TemperatureUnit unit);
```

The `TagName` attribute can be used to specify a custom tag name for each parameter or property. This is useful when you want to have more control over the naming of tags in your logs.

```csharp
[LoggerMessage(1, LogLevel.Information, "My custom tag name: {my.custom.tagname}")]
public static partial void LogMyCustomTagName(
    this ILogger logger,
    [TagName("my.custom.tagname")] string name);
```

### Log Enrichment

Logging data can be enriched by adding custom log enrichers to the service collection. This can be done using specific implementations or generic types.

```csharp
// Using a specific implementation
builder.Services.AddLogEnricher(new CustomLogEnricher());

// Using a generic type
builder.Services.AddLogEnricher<AnotherLogEnricher>();
```

Create custom log enrichers by implementing the `ILogEnricher` interface.

```csharp
public class CustomLogEnricher : ILogEnricher
{
    public void Enrich(IEnrichmentTagCollector collector)
    {
        // Add custom logic to enrich log data
        collector.Add("CustomTag", "CustomValue");
    }
}
```

### Latency Measurement

To track latency in an application it is possible to register checkpoint, measure, and tag names using the following methods:

```csharp
builder.Services.RegisterCheckpointNames("databaseQuery", "externalApiCall");
builder.Services.RegisterMeasureNames("responseTime", "processingTime");
builder.Services.RegisterTagNames("userId", "transactionId");
```

Implement the `ILatencyDataExporter` to export latency data. This can be integrated with external systems or logging frameworks.

```csharp
public class CustomLatencyDataExporter : ILatencyDataExporter
{
    public async Task ExportAsync(LatencyData data, CancellationToken cancellationToken)
    {
        // Export logic here
    }
}
```

Use the latency context to track performance metrics in your application.

```csharp
public void YourMethod(ILatencyContextProvider contextProvider)
{
    var context = contextProvider.CreateContext();
    var checkpointToken = context.GetCheckpointToken("databaseQuery");

    // Start measuring
    context.AddCheckpoint(checkpointToken);

    // Perform operations...

    // End measuring
    context.AddCheckpoint(checkpointToken);

    // Optionally, record measures and tags
    context.RecordMeasure(context.GetMeasureToken("responseTime"), measureValue);
    context.SetTag(context.GetTagToken("userId"), "User123");
}
```

### Http Request Metadata Handling

The `IDownstreamDependencyMetadata` interface is designed to capture and store metadata about the downstream dependencies of an HTTP request. This is particularly useful for understanding external service dependencies and their impact on your application's performance and reliability.

The `IOutgoingRequestContext` interface provides a mechanism for associating metadata with outgoing HTTP requests. This allows you to enrich outbound requests with additional information that can be used for logging, telemetry, and analysis.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
