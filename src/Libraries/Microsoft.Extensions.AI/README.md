# Microsoft.Extensions.AI

This package is supported on .NET Framework 4.6.2 and later.

.NET developers need to integrate and interact with a growing variety of artificial intelligence (AI) services in their apps. The `Microsoft.Extensions.AI` libraries provide a unified approach for representing generative AI components, and enable seamless integration and interoperability with various AI services.

## The packages

The [Microsoft.Extensions.AI.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions) package provides the core exchange types, including [`IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient) and [`IEmbeddingGenerator<TInput,TEmbedding>`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.iembeddinggenerator-2). Any .NET library that provides an LLM client can implement the `IChatClient` interface to enable seamless integration with consuming code.

The [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI) package has an implicit dependency on the `Microsoft.Extensions.AI.Abstractions` package. This package enables you to easily integrate components such as automatic function tool invocation, telemetry, and caching into your applications using familiar dependency injection and middleware patterns. For example, it provides the [`UseOpenTelemetry(ChatClientBuilder, ILoggerFactory, String, Action<OpenTelemetryChatClient>)`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.opentelemetrychatclientbuilderextensions.useopentelemetry#microsoft-extensions-ai-opentelemetrychatclientbuilderextensions-useopentelemetry(microsoft-extensions-ai-chatclientbuilder-microsoft-extensions-logging-iloggerfactory-system-string-system-action((microsoft-extensions-ai-opentelemetrychatclient)))) extension method, which adds OpenTelemetry support to the chat client pipeline.

## Which package to reference

Libraries that provide implementations of the abstractions typically reference only `Microsoft.Extensions.AI.Abstractions`.

To also have access to higher-level utilities for working with generative AI components, reference the `Microsoft.Extensions.AI` package instead (which itself references `Microsoft.Extensions.AI.Abstractions`). Most consuming applications and services should reference the `Microsoft.Extensions.AI` package along with one or more libraries that provide concrete implementations of the abstractions.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.AI
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.AI" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Documentation

Refer to the [Microsoft.Extensions.AI libraries documentation](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) for more information and API usage examples.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
