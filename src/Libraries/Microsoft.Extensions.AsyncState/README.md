# Microsoft.Extensions.AsyncState

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

This provides the ability to store and retrieve objects that flow with the current asynchronous context.
It has a few advantages over using the [`AsyncLocal<T>`](https://learn.microsoft.com/dotnet/api/system.threading.asynclocal-1) class directly:
- By abstracting the way the ambient data is stored we can use more optimized implementations, for instance when using ASP.NET Core, without exposing these components.
- Improves the performance by minimizing the number of `AsyncLocal<T>` instances required when multiple objects are shared.
- Provides a way to manage the lifetime of the ambient data objects.

> [!NOTE]
> Please note, the implementations of `IAsyncState` and `IAsyncContext<T>` are not thread-safe.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.AsyncState
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.AsyncState" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

### Registering Services

The services can be registered using the following method:

```csharp
public static IServiceCollection AddAsyncState(this IServiceCollection services)
```

### Consuming Services

The `IAsyncContext<T>` can be injected wherever async state is needed. For example:

```csharp
public class MyClass
{
  public MyClass(IAsyncContext<MyState> asyncContext) { Context = asyncContext }

  private IAsyncContext<MyState> Context { get; }

  public async Task DoWork()
  {
    var state = Context.Get();
    // or
    Context.Set(new MyState());
    // or
    if (Context.TryGet(out var state)) { ... }
  }
}
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
