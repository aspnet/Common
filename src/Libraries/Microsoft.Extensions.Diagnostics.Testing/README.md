# Microsoft.Extensions.Diagnostics.Testing

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

Hand-crafted fakes to make telemetry-related testing easier.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Diagnostics.Testing
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Fake logging

These components enable faking logging services for testing purposes.

When using this package, you can register a fake logging provider by one of the following methods:

```csharp
public static ILoggingBuilder AddFakeLogging(this ILoggingBuilder builder)
public static ILoggingBuilder AddFakeLogging(this ILoggingBuilder builder, IConfigurationSection section)
public static ILoggingBuilder AddFakeLogging(this ILoggingBuilder builder, Action<FakeLogCollectorOptions> configure)
```

You can also register fake logging in the service collection:

```csharp
public static IServiceCollection AddFakeLogging(this IServiceCollection services)
public static IServiceCollection AddFakeLogging(this IServiceCollection services, IConfigurationSection section)
public static IServiceCollection AddFakeLogging(this IServiceCollection services, Action<FakeLogCollectorOptions> configure)
```

After registering the fake logging services, you can resolve the fake logging provider with this method:

```csharp
public static FakeLogCollector GetFakeLogCollector(this IServiceProvider services)
```

You can also create an instance of `FakeLogger` using one of its constructors:

```csharp
public FakeLogger(FakeLogCollector? collector = null, string? category = null)
public FakeLogger(Action<string> outputSink, string? category = null)
```

You can then use it right away, for example:

```csharp
var fakeLogger = new FakeLogger<MyComponent>();

// Optionally, you can set the log level
// fakeLogger.ControlLevel(LogLevel.Debug, enabled: true);

var myComponentUnderTest = new MyComponent(fakeLogger);
myComponentUnderTest.DoWork(); // We assume that the component will produce some logs

FakeLogCollector collector = fakeLogger.Collector; // Collector allows you to access the captured logs
IReadOnlyList<FakeLogRecord> logs = collector.GetSnapshot();
// ... assert that the logs are correct
```

### Metric collector

The `MetricCollector` allows you to collect metrics in tests. It has a few constructors and you can choose the one that fits your needs:

```csharp
public MetricCollector(Instrument<T> instrument, TimeProvider? timeProvider = null)
public MetricCollector(ObservableInstrument<T> instrument, TimeProvider? timeProvider = null)
public MetricCollector(object? meterScope, string meterName, string instrumentName, TimeProvider? timeProvider = null)
public MetricCollector(Meter meter, string instrumentName, TimeProvider? timeProvider = null)
```

When you have an exact instrument, you can use the first two constructors. If you have a meter scope (typically it's [`IMeterFactory`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.imeterfactory?view=net-8.0)), you can use the third constructor. If you have a meter, use the last one.

Here is an example of how to use the `MetricCollector`:

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Testing;

using var meter = new Meter("TestMeter");
using var collector = new MetricCollector<int>(meter, "TestInstrument");

var myComponentUnderTest = new MyComponent(meter);
myComponentUnderTest.DoWork(); // We assume that the component will produce some integer metrics

CollectedMeasurement<int>? measurement = collector.LastMeasurement();
// ... assert that the measurement is correct
```

Please note that the `MetricCollector` is generic and you need to specify the type of the metric you want to collect (e.g. `int`, `double`, etc.).

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
