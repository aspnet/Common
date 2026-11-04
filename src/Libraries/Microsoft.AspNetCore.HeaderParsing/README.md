# Microsoft.AspNetCore.HeaderParsing

This package provides services for strongly typed header parsing and value caching.

In particular:

- There are many different headers with subtle format differences that are hard to parse correctly in all cases. This package provides predefined parsers for commonly used headers.
- Your application may parse the same header value multiple times per request. These APIs automatically cache the parsed values of these headers to make your application faster.
- This package also provides logging and metering of bad header values.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.AspNetCore.HeaderParsing
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.HeaderParsing" Version="[CURRENTVERSION]" />
</ItemGroup>
```
## Usage Example

### Registering the services

The services can be registered using one of the `AddHeaderParsing` overloads:

```csharp
public static IServiceCollection AddHeaderParsing(this IServiceCollection services)
public static IServiceCollection AddHeaderParsing(this IServiceCollection services, Action<HeaderParsingOptions> configure)
public static IServiceCollection AddHeaderParsing(this IServiceCollection services, IConfigurationSection section)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeaderParsing();

var app = builder.Build();
```

### Registering the parsers

For each header that you want to parse, you need to register an instance of a `HeaderSetup` class which will define how a named parser should be parsed, and if the resulting strongly-typed values should be cached in memory.

> [!NOTE]
> Instances of `HeaderSetup` for the most common headers are available in the `CommonHeaders` class.

> [!IMPORTANT]
> Instances of `HeaderSetup` should be reused during the application lifetime. It is recommended to keep their reference in objects registered as singletons.

```csharp
var headerRegistry = app.Services.GetRequiredService<IHeaderRegistry>();

var encodingKey = headerRegistry.Register(CommonHeaders.AcceptEncoding);
```

### Using the parsers

The following example parses the `Accept-Encoding` request header to return a list of values:

```csharp
app.MapGet("/", string (HttpContext context) =>
{
    if (context.Request.TryGetHeaderValue(encodingKey, out var encoding))
    {
        return string.Join(", ", encoding);
    }

    return "";
});
```

## List of available common parsers

The `CommonHeaders` class contains the following reusable header parsers:

- [Host](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.Host)
- [Accept](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.Accept)
- [AcceptEncoding](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.AcceptEncoding)
- [AcceptLanguage](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.AcceptLanguage)
- [CacheControl](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.CacheControl)
- [ContentDisposition](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.ContentDisposition)
- [ContentType](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.ContentType)
- [Cookie](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.Cookie)
- [Date](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.Date)
- [IfMatch](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.IfMatch)
- [IfModifiedSince](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.IfModifiedSince)
- [IfNoneMatch](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.IfNoneMatch)
- [IfRange](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.IfRange)
- [IfUnmodifiedSince](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.IfUnmodifiedSince)
- [Range](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.Range)
- [Referer](https://learn.microsoft.com/dotnet/api/microsoft.net.http.headers.headernames.Referer)
- XForwardedFor (`X-Forwarded-For`)

## Options

The `HeaderParsingOptions` class is used to configure common behaviors for this feature.

- You can pre-configure default values to be returned when the current HTTP request contains headers for which you have not configured parsing. See the `DefaultValues` option.
- You can change the number of cached values using the `MaxCachedValuesPerHeader` option. If not configured the `DefaultMaxCachedValuesPerHeader` value will be used.

## Metrics

The package generates the following metrics:

| Metric name | Metric value | Dimension | Dimension value |
|-|-|-|-|
| `aspnetcore.header_parsing.parse_errors`   | Increased by **1** with every parsing error     | `aspnetcore.header_parsing.header.name`       | Header name                                       |
|                                            |                                                 | `error.type`                                  | Error message returned by the `TryParse()` method |
| `aspnetcore.header_parsing.cache_accesses` | Increased by **1** with every cache hit or miss | `aspnetcore.header_parsing.header.name`       | Header name                                       |
|                                            |                                                 | `aspnetcore.header_parsing.cache_access.type` | Either `Hit` or `Miss` for the internal header cache hits and misses respectively |

## Custom headers parsing

Although this package provides support for parsing many HTTP headers, you sometimes need to parse custom headers.
You can easily extend the header parsing model to support your custom headers by implementing the abstract class `HeaderParser` and then register an 
instance of this class in the `IHeaderRegistry` interface.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
