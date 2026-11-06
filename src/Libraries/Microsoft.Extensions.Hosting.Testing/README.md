# Microsoft.Extensions.Hosting.Testing

Tools for integration testing of apps built with Microsoft.Extensions.Hosting.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Hosting.Testing
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting.Testing" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### FakeHost

`FakeHost` enables creating an application host pre-configured for a unit test environment.

The host can be created using the following APIs:

```csharp
public static IHostBuilder CreateBuilder()
public static IHostBuilder CreateBuilder(Action<FakeHostOptions> configure)
public static IHostBuilder CreateBuilder(FakeHostOptions options)
```

For example:

```csharp
using var host = await FakeHost.CreateBuilder(options => { });
    .ConfigureServices(x =>
    {
        // ...
    })
    .StartAsync();
```

### FakeHostingExtensions

`FakeHostingExtensions` is a collection of [`IHost`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.hosting.ihost) and [`IHostBuilder`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.hosting.ihostbuilder) extension methods for use within a unit test environment. These help collect logs as well as augment the host and app configurations.

## Feedback & Contributing

For any feedback or contributions, please visit us in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
