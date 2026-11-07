# Microsoft.Extensions.Http.Resilience

Resilience mechanisms for `HttpClient` built on the [Polly framework](https://www.pollydocs.org/).

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.Http.Resilience
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Examples

When configuring an HttpClient through the [HTTP client factory](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory) the following extensions can add a set of pre-configured hedging or resilience behaviors. These pipelines combine multiple resilience strategies with pre-configured defaults.
- The total request timeout pipeline applies an overall timeout to the execution, ensuring that the request including hedging attempts, does not exceed the configured limit.
- The retry pipeline retries the request in case the dependency is slow or returns a transient error.
- The rate limiter pipeline limits the maximum number of requests being send to the dependency.
- The circuit breaker blocks the execution if too many direct failures or timeouts are detected.
- The attempt timeout pipeline limits each request attempt duration and throws if its exceeded.

### Resilience

The standard resilience pipeline makes use of the above strategies to ensure HTTP requests can be sent reliably.

```csharp
var clientBuilder = services.AddHttpClient("MyClient");

clientBuilder.AddStandardResilienceHandler().Configure(o =>
{
    o.CircuitBreaker.MinimumThroughput = 10;
});
```

### Hedging

The standard hedging pipeline uses a pool of circuit breakers to ensure that unhealthy endpoints are not hedged against. By default, the selection from pool is based on the URL Authority (scheme + host + port). It is recommended that you configure the way the strategies are selected by calling the `SelectPipelineByAuthority()` extensions. The last three strategies are applied to each individual endpoint.

```csharp
var clientBuilder = services.AddHttpClient("MyClient");

clientBuilder.AddStandardHedgingHandler().Configure(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
});
```

### Retry Budget

A retry budget limits retries to a ratio of the requests sent over a sliding window. When a dependency is degraded, retries can multiply the load on it by up to `MaxRetryAttempts + 1`; a budget caps that amplification to roughly `1 + RetryRatio`. When the budget is exhausted, the failed outcome is returned without a retry and the `OnRetryBudgetExhausted` resilience event is reported.

`UseBudgetedRetries()` enables the budget with its default values. It only changes the retry budget and composes with other retry extensions, such as `DisableForUnsafeHttpMethods()`. Values set afterwards take precedence.

```csharp
var clientBuilder = services.AddHttpClient("MyClient");

clientBuilder.AddStandardResilienceHandler().UseBudgetedRetries();
```

Options are applied in registration order, so call `UseBudgetedRetries()` before `Configure(...)` to let configuration override the budget values:

```csharp
clientBuilder
    .AddStandardResilienceHandler()
    .UseBudgetedRetries()
    .Configure(configuration.GetSection("MyClient:Resilience"));
```

It can also be applied to the retry options directly, for example to combine it with other retry changes:

```csharp
clientBuilder.AddStandardResilienceHandler().Configure(o =>
{
    o.Retry.UseBudgetedRetries();
    o.Retry.DisableForUnsafeHttpMethods();
});
```

In a custom pipeline, use `AddRetryWithBudget` to add retries with the budget, without circuit breakers, timeouts or rate limiting. Polly's `AddRetry` can't apply the budget, so it throws a `ValidationException` when `HttpRetryStrategyOptions.RetryBudget` is set, whether directly, through `UseBudgetedRetries`, or from configuration. When `RetryBudget` isn't set, `AddRetryWithBudget` uses the default budget:

```csharp
clientBuilder.AddResilienceHandler("retry", b =>
{
    b.AddRetryWithBudget(new HttpRetryStrategyOptions());
});
```

Set `RetryBudget` to customize the budget:

```csharp
clientBuilder.AddResilienceHandler("myHandler", b =>
{
    b.AddRetryWithBudget(new HttpRetryStrategyOptions
    {
        RetryBudget = new HttpRetryBudgetOptions
        {
            RetryRatio = 0.2,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 20,
        },
    });
});
```

These APIs are experimental and require suppressing the `EXTEXP0001` diagnostic.

Each retry that the budget denies is logged as a warning and counted by the `resilience.polly.strategy.events` metric (meter `Polly`) with the `event.name` tag set to `OnRetryBudgetExhausted`. The `pipeline.name`, `strategy.name` and `error.type` tags identify the client and the failure that wasn't retried, so you can alert when retries are being shed.

Telemetry listeners can inspect the event's immutable `OnRetryBudgetExhaustedArguments` to get the zero-based `AttemptNumber` whose retry was denied.

### Custom Resilience

For more granular control a custom pipeline can be constructed.

```csharp
var clientBuilder = services.AddHttpClient("MyClient");

clientBuilder.AddResilienceHandler("myHandler", b =>
{
    b.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>()
    {
        FallbackAction = _ => Outcome.FromResultAsValueTask(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
    })
    .AddConcurrencyLimiter(100)
    .AddRetry(new HttpRetryStrategyOptions())
    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions())
    .AddTimeout(new HttpTimeoutStrategyOptions());
});
```

## Known issues

The following sections detail various known issues.

### Compatibility with the `Grpc.Net.ClientFactory` package

If you're using `Grpc.Net.ClientFactory` version `2.63.0` or earlier, then enabling the standard resilience or hedging handlers for a gRPC client could cause a runtime exception. Specifically, consider the following code sample:

```csharp
services
    .AddGrpcClient<Greeter.GreeterClient>()
    .AddStandardResilienceHandler();
```

The preceding code results in the following exception:

```Output
System.InvalidOperationException: The ConfigureHttpClient method is not supported when creating gRPC clients. Unable to create client with name 'GreeterClient'.
```

To resolve this issue, we recommend upgrading to `Grpc.Net.ClientFactory` version `2.64.0` or later.

There's a build time check that verifies if you're using `Grpc.Net.ClientFactory` version `2.63.0` or earlier, and if you are the check produces a compilation warning. You can suppress the warning by setting the following property in your project file:

```xml
<PropertyGroup>
  <SuppressCheckGrpcNetClientFactoryVersion>true</SuppressCheckGrpcNetClientFactoryVersion>
</PropertyGroup>
```

### Compatibility with .NET Application Insights

If you're using .NET Application Insights version **2.22.0** or lower, then enabling resilience functionality in your application could cause all Application Insights telemetry to be missing. The issue occurs when resilience functionality is registered before Application Insights services. Consider the following sample causing the issue:

```csharp
// At first, we register resilience functionality.
services.AddHttpClient().AddStandardResilienceHandler();

// And then we register Application Insights. As a result, Application Insights doesn't work.
services.AddApplicationInsightsTelemetry();
```

The issue can be fixed by updating .NET Application Insights to version **2.23.0** or higher. If you cannot update it, then registering Application Insights services before resilience functionality, as shown below, will fix the issue:

```csharp
// We register Application Insights first, and now it will be working correctly.
services.AddApplicationInsightsTelemetry();
services.AddHttpClient().AddStandardResilienceHandler();
```

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
