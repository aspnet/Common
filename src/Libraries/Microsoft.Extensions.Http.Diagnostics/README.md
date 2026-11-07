# Microsoft.Extensions.Http.Diagnostics

Telemetry support for `HttpClient` that allows tracking latency and enriching and redacting log output for structured logs.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Http.Diagnostics
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Http.Diagnostics" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### HTTP Client Logs Enrichment and Redaction

These components enable enriching and redacting `HttpClient` request logs. They remove built-it HTTP Client logging.

When using this package, some of the log properties are redacted by default (like full routes). `AddExtendedHttpClientLogging()` registers a default redactor provider by calling `AddRedaction()` from the `Microsoft.Extensions.Compliance.Redaction` package. The default redactor provider erases any data that has a data classification other than `DataClassification.None`. To use specific redactors for your data classifications, call `builder.Services.AddRedaction(...)` and configure them; this can be done before or after calling `AddExtendedHttpClientLogging()`. If you use `AddFakeRedaction()` or a custom `IRedactorProvider` implementation, it must be registered before calling `AddExtendedHttpClientLogging()`.

The http client logging services can be registered using the following methods:

```csharp
public static IServiceCollection AddExtendedHttpClientLogging(this IServiceCollection services)
public static IServiceCollection AddExtendedHttpClientLogging(this IServiceCollection services, IConfigurationSection section)
public static IServiceCollection AddExtendedHttpClientLogging(this IServiceCollection services, Action<LoggingOptions> configure)
public static IServiceCollection AddHttpClientLogEnricher<T>(this IServiceCollection services) where T : class, IHttpClientLogEnricher
```

For example:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Register IHttpClientFactory:
builder.Services.AddHttpClient();

// Register HttpClient logging enrichment & redaction services:
builder.Services.AddExtendedHttpClientLogging();

// Register a logging enricher (the type should implement IHttpClientLogEnricher):
builder.Services.AddHttpClientLogEnricher<MyHttpClientLogEnricher>();

var host = builder.Build();
```

It is important to note that the `AddExtendedHttpClientLogging` method will add information to the logs using *enrichment*. This means that the information will be added as tags to the structured logs, but will not be visible in the log message that is printed by default in the console. To view the information, you will need to use a logging provider that supports structured logs. One quick and built-in way to do this, is to call `AddJsonConsole()` to your logging builder, which will print out the full structured logs to the console. Here is a quick sample that uses the `ExtendedHttpClientLogging()` method to automatically log all `HttpClient` request and response bodies, and then prints the full structured logs to the console:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

services.AddLogging(o => o.SetMinimumLevel(LogLevel.Trace).AddJsonConsole()); // <-- Enable structured logging to the console

services.AddHttpClient("foo")
    .AddExtendedHttpClientLogging(o =>
    {
        // Enable logging of request and response bodies:
        o.LogBody = true;

        // We also need to specify the content types that we want to log:
        o.ResponseBodyContentTypes.Add("application/json");
    });

var sp = services.BuildServiceProvider();

var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("foo");

var response = await client.GetAsync(new Uri("https://httpbin.org/json")).ConfigureAwait(false);
```

By default, request and response routes are redacted for privacy reasons. You can change this behavior by making use of the `RequestPathParameterRedactionMode` option like:

```csharp
  .AddExtendedHttpClientLogging(o =>
{
    //.. Other options

    o.RequestPathParameterRedactionMode = HttpRouteParameterRedactionMode.None; // <-- Disable redaction of request/response routes
});
```

You can also use the following extension methods to apply the logging to the specific `IHttpClientBuilder`:

```csharp
public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder)
public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, IConfigurationSection section)
public static IHttpClientBuilder AddExtendedHttpClientLogging(this IHttpClientBuilder builder, Action<LoggingOptions> configure)
```

For example:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Register named HttpClient:
var httpClientBuilder = builder.Services.AddHttpClient("MyNamedClient");

// Configure named HttpClient to use logging enrichment & redaction:
httpClientBuilder.AddExtendedHttpClientLogging();

var host = builder.Build();
```

### Tracking HTTP Request Client Latency

These components enable tracking and reporting the latency of HTTP Client request processing.

The services can be registered using the following methods. They also register the latency context services (see `AddLatencyContext()`) unless those are already registered:

```csharp
public static IServiceCollection AddHttpClientLatencyTelemetry(this IServiceCollection services)
public static IServiceCollection AddHttpClientLatencyTelemetry(this IServiceCollection services, IConfigurationSection section)
public static IServiceCollection AddHttpClientLatencyTelemetry(this IServiceCollection services, Action<HttpClientLatencyTelemetryOptions> configure)
```

For example:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Register IHttpClientFactory:
builder.Services.AddHttpClient();

// Register HttpClient logging enrichment & redaction services:
builder.Services.AddExtendedHttpClientLogging();

// Register HttpClient latency telemetry services:
builder.Services.AddHttpClientLatencyTelemetry();

var host = builder.Build();
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
