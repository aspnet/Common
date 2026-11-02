# Microsoft.Extensions.Caching.Hybrid

This package contains a concrete implementation of [the `HybridCache` API](https://learn.microsoft.com/dotnet/api/microsoft.extensions.caching.hybrid),
simplifying and enhancing cache usage that might previously have been built on top of [`IDistributedCache`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.caching.distributed.idistributedcache).

Key features:

- built on top of `IDistributedCache` - all existing cache backends (Redis, SQL Server, CosmosDB, etc) should work immediately
- simple API (all the cache, serialization, etc details from are encapsulated)
- cache-stampede protection (combining of concurrent requests for the same data)
- performance enhancements such as inbuilt support for the newer [`IBufferDistributedCache`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.caching.distributed.ibufferdistributedcache) API
- fully configurable serialization

Full `HybridCache` documentation is [here](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid).

## Full documentation

See [learn.microsoft.com](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid) for full discussion of `HybridCache`.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Caching.Hybrid" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage example

The `HybridCache` service can be registered and configured via `IServiceCollection`, for example:

```csharp
builder.Services.AddHybridCache(/* optional configuration /*);
```

Note that in many cases you may also wish to register a distributed cache backend, as
[discussed here](https://learn.microsoft.com/aspnet/core/performance/caching/distributed); for example
a Redis instance:

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("MyRedisConStr");
});
```

Once registered, the `HybridCache` instance can be obtained via dependency-injection, allowing the
`GetOrCreateAsync` API to be used to obtain data:

```csharp
public class SomeService(HybridCache cache)
{
    private HybridCache _cache = cache;

    public async Task<SomeDataType> GetSomeInfoAsync(string name, int id, CancellationToken token = default)
    {
        return await _cache.GetOrCreateAsync(
            $"{name}-{id}", // Unique key to the cache entry
            async cancel => await GetDataFromTheSourceAsync(name, id, cancel),
            cancellationToken: token
        );
    }

    private async Task<SomeDataType> GetDataFromTheSourceAsync(string name, int id, CancellationToken token)
    {
        // talk to the underlying data store here - could be SQL, gRPC, HTTP, etc
    }
}
```

To read a value that may already be cached, without creating it when it is missing, use the `GetAsync`
extension method; a miss is never written to the cache:

```csharp
var (found, info) = await _cache.GetAsync<SomeDataType>($"{name}-{id}", cancellationToken: token);
if (found)
{
    // use info
}
```

Additional usage guidance - including expiration, custom serialization support, and alternate usage
to reduce delegate allocation - is available
on [learn.microsoft.com](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid).
