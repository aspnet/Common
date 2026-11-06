# Microsoft.Extensions.TimeProvider.Testing

This package is not supported on .NET Framework. Compatibility issues may be closed as not planned.

Provides a `FakeTimeProvider` for testing components that depend on `System.TimeProvider`.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.TimeProvider.Testing
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Usage Example

`FakeTimeProvider` can be used to manually adjust time to test time dependent components in a deterministic way.

`FakeTimeProvider` derives from [TimeProvider](https://learn.microsoft.com/dotnet/api/system.timeprovider) and adds the following APIs:

```csharp
public FakeTimeProvider(DateTimeOffset startDateTime)
public DateTimeOffset Start { get; }
public TimeSpan AutoAdvanceAmount { get; set; }
public void SetUtcNow(DateTimeOffset value)
public void Advance(TimeSpan delta)
public void SetLocalTimeZone(TimeZoneInfo localTimeZone)
```

### `ExpiryCache` with `TimeProvider`

The example below demonstrates the `ExpiryCache` class and how it can be tested using `FakeTimeProvider` in `ExpiryCacheTests`. 

The `TimeProvider` abstraction is injected into the `ExpiryCache` class, allowing the cache to rely on `GetUtcNow()` to determine whether cache entries should be evicted based on the current time. This abstraction provides flexibility by enabling different time-related behaviors in test environments.

By using `FakeTimeProvider` in testing, we can simulate the passage of time with methods like `Advance()` and `SetUtcNow()`. This makes it possible to emulate the system's time in a controlled and predictable way during tests, ensuring that cache eviction works as expected.

```csharp
public class ExpiryCache<TKey, TValue>
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TKey, CacheItem> _cache = new();
    private readonly TimeSpan _expirationDuration;

    public ExpiryCache(TimeProvider timeProvider, TimeSpan expirationDuration)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _expirationDuration = expirationDuration;
    }

    public void Add(TKey key, TValue value)
    {
        var expirationTime = _timeProvider.GetUtcNow() + _expirationDuration;
        var cacheItem = new CacheItem(value, expirationTime);

        _cache[key] = cacheItem;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        value = default;
        if (_cache.TryGetValue(key, out TValue cacheItem))
        {
            if (cacheItem.ExpirationTime > _timeProvider.GetUtcNow())
            {
                value = cacheItem.Value;
                return true;
            }

            // Remove expired item
            _cache.TryRemove(key, out _);
        }
        return false;
    }

    private class CacheItem
    {
        public TValue Value { get; }
        public DateTimeOffset ExpirationTime { get; }

        public CacheItem(TValue value, DateTimeOffset expirationTime)
        {
            Value = value;
            ExpirationTime = expirationTime;
        }
    }
}

using Microsoft.Extensions.Time.Testing;

public class ExpiryCacheTests
{
    [Fact]
    public void ExpiryCache_ShouldRemoveExpiredItems()
    {
        var timeProvider = new FakeTimeProvider();
        var cache = new ExpiryCache<string, string>(timeProvider, TimeSpan.FromSeconds(3));

        cache.Add("key1", "value1");

        // Simulate time passing
        timeProvider.SetUtcNow(timeProvider.GetUtcNow() + TimeSpan.FromSeconds(2));

        // The item should still be in the cache
        bool found = cache.TryGetValue("key1", out string value);
        Assert.True(found);
        Assert.Equal("value1", value);

        // Simulate further time passing to be after expiration time
        timeProvider.SetUtcNow(timeProvider.GetUtcNow() + TimeSpan.FromSeconds(2));

        // The item should now be expired
        found = cache.TryGetValue("key1", out value);
        Assert.False(found);
    }
}
```

## `SynchronizationContext` in xUnit Tests

### xUnit v2

Some testing libraries such as xUnit v2 provide custom `SynchronizationContext` for running tests. xUnit v2, for instance, provides `AsyncTestSyncContext` that allows to properly manage asynchronous operations within the test execution. However, it brings an issue when we test asynchronous code that uses `ConfigureAwait(false)` in combination with class like `FakeTimeProvider`. In such cases, the xUnit context may lose track of the continuation, causing the test to become unresponsive, whether the test itself is asynchronous or not.

To prevent this issue, remove the xUnit context for tests dependent on `FakeTimeProvider` by setting the synchronization context to `null`:
```csharp
SynchronizationContext.SetSynchronizationContext(null)
```

The `Advance` method is used to simulate the passage of time. Below is an example how to create a test for a code that uses `ConfigureAwait(false)` that ensures that the continuation of the awaited task (i.e., the code that comes after the await statement) works correctly. For a more realistic example, consider the following test using Polly:

```csharp
using Polly;
using Polly.Retry;

public class SomeService(TimeProvider timeProvider)
{
    // Don't do this in real life, not thread safe
    public int Tries { get; private set; }

    private readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
        .AddRetry(
            new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>(),
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Linear,
            })
        .Build();

    public async Task<int> PollyRetry(double taskDelay, double cancellationSeconds)
    {
        Tries = 0;
        return await _retryPipeline.ExecuteAsync(
            async _ =>
            {
                Tries++;
                // Simulate a task that takes some time to complete
                // With xUnit Context this would fail.
                await timeProvider.Delay(TimeSpan.FromSeconds(taskDelay)).ConfigureAwait(false);
                if (Tries < 2)
                {
                    throw new InvalidOperationException();
                }
                return Tries;
            },
            CancellationToken.None);
    }
}

using Microsoft.Extensions.Time.Testing;

public class SomeServiceTests
{
    [Fact]
    public void PollyRetry_ShouldHave2Tries()
    {
        // Arrange
        // Remove xUnit Context for this test
        SynchronizationContext.SetSynchronizationContext(null);
        var timeProvider = new FakeTimeProvider();
        var someService = new SomeService(timeProvider);

        // Act
        var result = someService.PollyRetry(taskDelay: 1, cancellationSeconds: 6);

        // Advancing the time more than one second should resolves the first execution delay.
        timeProvider.Advance(TimeSpan.FromMilliseconds(1001));

        // Advancing the time more than the retry delay time of 1s,
        // and less then the task execution delay should start the second try
        timeProvider.Advance(TimeSpan.FromMilliseconds(1050));
        
        // Assert
        result.IsCompleted.Should().BeFalse();
        someService.Tries.Should().Be(2);
    }
}
```

### xUnit v3 

`AsyncTestSyncContext` has been removed, more info [here](https://xunit.net/docs/getting-started/v3/migration), so above issue is no longer a problem.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
