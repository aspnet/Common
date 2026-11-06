# Microsoft.Extensions.Resilience

Extensions to the Polly libraries to enrich telemetry with metadata and exception summaries.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Resilience
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Resilience" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Examples

The services can be registered using the following method:

```csharp
public static IServiceCollection AddResilienceEnricher(this IServiceCollection services)
```

This will optionally consume the `IExceptionSummarizer` service if it has been registered and add that data to Polly's telemetry. It will also include `RequestMetadata` that can be set or retrieved with these extensions:

```csharp
public static void SetRequestMetadata(this ResilienceContext context, RequestMetadata requestMetadata)
public static RequestMetadata? GetRequestMetadata(this ResilienceContext context)
```

See the Polly docs for details about working with [`ResilienceContext`](https://www.pollydocs.org/advanced/resilience-context.html).

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
