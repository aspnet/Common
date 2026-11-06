# Microsoft.Extensions.Options.Contextual

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

APIs for dynamically configuring options based on a given context.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Options.Contextual
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Options.Contextual" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

Start with an option type.

```csharp
internal class WeatherForecastOptions
{
    public string TemperatureScale { get; set; } = "Celsius"; // Celsius or Fahrenheit
    public int ForecastDays { get; set; }
}
```

Define a context and a receiver that will be used as inputs to dynamically configure the options.

```csharp
[OptionsContext]
internal partial class WeatherForecastContext // Note class must be partial
{
    public Guid UserId { get; set; }
    public string? Country { get; set; }
}

internal class CountryContextReceiver : IOptionsContextReceiver
{
    public string? Country { get; private set; }

    public void Receive<T>(string key, T value)
    {
        if (key == nameof(Country))
        {
            Country = value?.ToString();
        }
    }
}
```

Create a service that consumes the options for a given context.

```csharp
internal class WeatherForecast
{
    public DateTime Date { get; set; }
    public int Temperature { get; set; }
    public string TemperatureScale { get; set; } = string.Empty;
}

internal class WeatherForecastService
{
    private readonly IContextualOptions<WeatherForecastOptions> _contextualOptions;
    private readonly Random _rng = new(0);

    public WeatherForecastService(IContextualOptions<WeatherForecastOptions> contextualOptions)
    {
        _contextualOptions = contextualOptions;
    }

    public async Task<IEnumerable<WeatherForecast>> GetForecast(WeatherForecastContext context, CancellationToken cancellationToken)
    {
        WeatherForecastOptions options = await _contextualOptions.GetAsync(context, cancellationToken).ConfigureAwait(false);
        return Enumerable.Range(1, options.ForecastDays).Select(index => new WeatherForecast
        {
            Date = new DateTime(2000, 1, 1).AddDays(index),
            Temperature = _rng.Next(-20, 55),
            TemperatureScale = options.TemperatureScale,
        });
    }
}
```

The options can be configured with both global options (ForecastDays), and options that vary depending on the current context (TemperatureScale).

```csharp
using var host = FakeHost.CreateBuilder()
    .ConfigureServices(services => services
        .Configure<WeatherForecastOptions>(options => options.ForecastDays = 7)
        .Configure<WeatherForecastOptions>(ConfigureTemperatureScaleBasedOnCountry)
        .AddSingleton<WeatherForecastService>())
        .Build();

static void ConfigureTemperatureScaleBasedOnCountry(IOptionsContext context, WeatherForecastOptions options)
{
    CountryContextReceiver receiver = new();
    context.PopulateReceiver(receiver);
    if (receiver.Country == "US")
    {
        options.TemperatureScale = "Fahrenheit";
    }
}
```

And lastly, the service is called with some context.

```csharp
var forecastService = host.Services.GetRequiredService<WeatherForecastService>();

var usForcast = await forecastService.GetForecast(new WeatherForecastContext { Country = "US" }, CancellationToken.None);
var caForcast = await forecastService.GetForecast(new WeatherForecastContext { Country = "CA" }, CancellationToken.None);
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
