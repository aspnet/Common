# Microsoft.Extensions.AmbientMetadata.Application

This flows runtime information for application-level ambient metadata such as the version, deployment ring, environment, and name. This information can be useful to enrich telemetry.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.AmbientMetadata.Application
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.AmbientMetadata.Application" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Registering Services

The services can be registered using any of the following methods:

```csharp
public static IHostBuilder UseApplicationMetadata(this IHostBuilder builder, string sectionName = DefaultSectionName)
public static TBuilder UseApplicationMetadata<TBuilder>(this TBuilder builder, string sectionName = DefaultSectionName) where TBuilder : IHostApplicationBuilder
public static IServiceCollection AddApplicationMetadata(this IServiceCollection services, Action<ApplicationMetadata> configure)
```

### Configuration

When loading from configuration, the version and deployment ring metadata are read from the `ambientmetadata:application` section. The environment and application names are read from the `IHostEnvironment`.

```json
{
  "AmbientMetadata" {
    "Application" {
      "BuildVersion": "1.0-alpha1.2346",
      "DeploymentRing": "InnerRing"
    }
  }
}
```

### Consuming Services

The `ApplicationMetadata` can be injected wherever needed. For example:

```csharp
public class MyClass
{
  public MyClass(IOptions<ApplicationMetadata> options) { Application = options.Value; }

  private ApplicationMetadata Application { get; }

  public void DoWork()
  {
    Log.LogEnvironment(Application.Version, Application.DeploymentRing, Application.Environment, Application.Name);
  }
}
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
