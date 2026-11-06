# Microsoft.Extensions.Diagnostics.ResourceMonitoring

Measures and reports processor and memory usage. To monitor system resources, this library:

- Utilizes control groups (cgroups) in Linux. Both cgroups v1 and v2 are supported.
- Utilized Job Objects in Windows.
- Mac OS is not supported.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Diagnostics.ResourceMonitoring
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.ResourceMonitoring" Version="[CURRENTVERSION]" />
</ItemGroup>
```


## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).

## Supported Platforms

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.
