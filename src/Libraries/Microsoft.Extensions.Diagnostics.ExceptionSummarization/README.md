# Microsoft.Extensions.Diagnostics.ExceptionSummarization

This provides the ability to extract essential information from well-known exception types and return a single string that can be used to create low-cardinality diagnostic messages for use in telemetry.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Diagnostics.ExceptionSummarization
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.ExceptionSummarization" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Registering Services

The services can be registered using the following methods:

```csharp
public static IServiceCollection AddExceptionSummarizer(this IServiceCollection services)
public static IServiceCollection AddExceptionSummarizer(this IServiceCollection services, Action<IExceptionSummarizationBuilder> configure)
```

For example:

```csharp
services.AddExceptionSummarizer();
```

The package comes with a predefined `IExceptionSummaryProvider` implementation that handles common
exceptions that might be sent during a web request. Here is how to register it:

```csharp
using Microsoft.Extensions.Diagnostics.ExceptionSummarization;

services.AddExceptionSummarizer(b => b.AddHttpProvider());
```

### Consuming Services

Once registered, the `IExceptionSummarizer` class can be resolved. For example:

```csharp
using Microsoft.Extensions.Diagnostics.ExceptionSummarization;

var summarizer = services.BuildServiceProvider().GetRequiredService<IExceptionSummarizer>();

try
{
    throw new SocketException((int)SocketError.NetworkDown);
}
catch (Exception e)
{
    ExceptionSummary summary = summarizer.Summarize(e);

    Console.WriteLine(summary.Description); // writes NetworkDown
}
```

The `ExceptionSummary.Description` property never includes sensitive information and can be safely used.
As opposed to `ExceptionSummary.AdditionalDetails` which can contain sensitive information that should not be stored and can only be used for debugging purpose.

## Custom exception summarization

Custom implementations of the `IExceptionSummaryProvider` interface can be used to provide a summary for any type of exception.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
