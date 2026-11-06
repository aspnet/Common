# Microsoft.Extensions.Compliance.Testing

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

This package provides test fakes for testing data classification and redaction.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Compliance.Testing
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Compliance.Testing" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage

### Fake Redactor

The `FakeRedactor` class provides options and services to verify redaction events.

The fake redactor services can be registered using one of the `AddFakeRedaction` overloads:

```csharp
public static IServiceCollection AddFakeRedaction(this IServiceCollection services)
public static IServiceCollection AddFakeRedaction(this IServiceCollection services, Action<FakeRedactorOptions> configure)
```

For example:

```csharp
IServiceCollection services = new ServiceCollection();
services = services.AddFakeRedaction(options => options.RedactionFormat = "Redacted: {0}");
```

It also registers an instance of `Microsoft.Extensions.Compliance.Testing.FakeRedactionCollector` which can be used to inspect what was redacted.

For example:

```csharp
var serviceProvider = services.BuildServiceProvider();
var collector = serviceProvider.GetRequiredService<FakeRedactionCollector>();
Console.WriteLine(collector.AllRedactorRequests.Count); 
```

### Fake Taxonomy

The `Microsoft.Extensions.Compliance.Testing.FakeTaxonomy` taxonomy class contains simple data classification values to use while testing redaction.

It consists of these data classification properties:

- `FakeTaxonomy.PublicData`
- `FakeTaxonomy.PrivateData`

These properties have their corresponding data classification attributes that can be used on method arguments and properties when features require it:

- `[PublicData]` usually represents some data that should not be redacted.
- `[PrivateData]` usually extensions to setup that should be redacted.

Example:

```csharp
var redactionProvider = serviceProvider.GetFakeRedactionCollector();
var redactor = redactionProvider.GetRedactor(FakeTaxonomy.PublicData);
Console.WriteLine(redactor.Redact("Hello")); // "Redacted: Hello"
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
