# Microsoft.Extensions.ObjectPool.DependencyInjection

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

This provides the ability to retrieve pooled instances that can be initialized using dependency injection.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.ObjectPool.DependencyInjection
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.ObjectPool.DependencyInjection" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Registering Pools

The object pools can be registered using the following methods:

```csharp
public static IServiceCollection AddPooled<TService>(this IServiceCollection services, Action<DependencyInjectionPoolOptions>? configure = null)
public static IServiceCollection AddPooled<TService, TImplementation>(this IServiceCollection services, Action<DependencyInjectionPoolOptions>? configure = null)
```

For example:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MyService>();
builder.Services.AddPooled<MyPooledClass>();

var app = builder.Build();
```

### Consuming Pools

Once registered, pools can be resolved using dependency injection. For example:

```csharp
var pool = context.RequestServices.GetRequiredService<ObjectPool<MyPooledClass>>();

var obj = pool.Get();

// Use the pooled object ...

pool.Return(obj);
```

Pooled instances will be resolved from the root dependency injection container and can only
use singleton dependencies.

Pooled instances can implement `Microsoft.Extensions.ObjectPool.IResettable` in order to
be initialized when they are returned to the pool.

```csharp
public class MyPooledClass : IResettable
{
    private MyService _myService;

    public MyPooledClass(MyService myService)
    {
        _myService = myService;
    }

    public bool TryReset()
    {
        // Clean instance here
        return true;
    }
}
```

## Options

The `DependencyInjectionPoolOptions.Capacity` property is used to configure the maximum capacity of each pool. The default value is `1024`.

This value can also be set during the pool registration:

```csharp
builder.Services.AddPooled<MyPooledClass>(options => options.Capacity = 64);
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).