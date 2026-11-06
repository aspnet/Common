# Microsoft.Extensions.Compliance.Redaction

A redaction engine and canonical redactors.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Compliance.Redaction
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Compliance.Redaction" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Registering the services

The services can be registered using one of the `AddRedaction` overloads:

```csharp
public static IServiceCollection AddRedaction(this IServiceCollection services)
public static IServiceCollection AddRedaction(this IServiceCollection services, Action<IRedactionBuilder> configure)
```

### Configuring a redactor

Redactors are fetched at runtime using an `IRedactorProvider`. You can choose to implement your own provider and register it inside the `AddRedaction` call, or alternatively you can just use the default provider. Redactors can be configured using one of these `IRedactionBuilder` extension methods:

```csharp
// Using the default redactor provider:
builder.Services.AddRedaction(redactionBuilder =>
{
    // Assigns a redactor to use for a set of data classifications.
    redactionBuilder.SetRedactor<MyRedactor>(MySensitiveDataClassification);
    // Assigns a fallback redactor to use when processing classified data for which no specific redactor has been registered. 
    // The `ErasingRedactor` is the default fallback redactor. If no redactor is configured for a data classification then the data will be erased.
    redactionBuilder.SetFallbackRedactor<MyFallbackRedactor>();
});

// Using a custom redactor provider:
builder.Services.AddSingleton<IRedactorProvider, MyRedactorProvider>();
builder.Services.AddRedaction(redactionBuilder => { });
```

### Configuring the HMAC redactor

The HMAC redactor can be configured using one these `IRedactionBuilder` extension methods:

```csharp
public static IRedactionBuilder SetHmacRedactor(this IRedactionBuilder builder, Action<HmacRedactorOptions> configure, params DataClassificationSet[] classifications)
public static IRedactionBuilder SetHmacRedactor(this IRedactionBuilder builder, IConfigurationSection section, params DataClassificationSet[] classifications)
```

The `HmacRedactorOptions` requires its `KeyId` and `Key` properties to be set. The `HmacRedactor` is still in the experimental phase, which means that the above two methods will show warning `EXTEXP0002` notifying you that the `HmacRedactor` is not yet stable. In order to use it, you will need to either add `<NoWarn>$(NoWarn);EXTEXP0002</NoWarn>` to your project file or add `#pragma warning disable EXTEXP0002` around the calls to `SetHmacRedactor`.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
