// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.ServiceDiscovery.Internal;
using Xunit;

namespace Microsoft.Extensions.ServiceDiscovery.Tests;

/// <summary>
/// Tests for <see cref="LinkedChangeToken"/> and for the change token registrations which endpoint resolution
/// leaves behind on the tokens its providers contribute.
/// </summary>
public class LinkedChangeTokenTests
{
    [Fact]
    public void ActiveChangeCallbacks_IsTrue_WhenAnySourceRaisesCallbacks()
    {
        Assert.False(new LinkedChangeToken([]).ActiveChangeCallbacks);
        Assert.False(new LinkedChangeToken([new PassiveChangeToken()]).ActiveChangeCallbacks);
        Assert.True(new LinkedChangeToken([new PassiveChangeToken(), new TrackingChangeToken()]).ActiveChangeCallbacks);
    }

    [Fact]
    public void HasChanged_PollsSources_WhichDoNotRaiseCallbacks()
    {
        var passive = new PassiveChangeToken();
        var token = new LinkedChangeToken([passive]);

        Assert.False(token.HasChanged);

        passive.HasChanged = true;
        Assert.True(token.HasChanged);
    }

    [Fact]
    public void RegisterChangeCallback_IsInvoked_WhenAnySourceSignals()
    {
        var first = new TrackingChangeToken();
        var second = new TrackingChangeToken();
        var token = new LinkedChangeToken([first, second]);

        var signaled = 0;
        using var registration = token.RegisterChangeCallback(_ => signaled++, null);

        Assert.Equal(0, signaled);
        Assert.False(token.HasChanged);

        second.Signal();

        Assert.Equal(1, signaled);
        Assert.True(token.HasChanged);

        // Signalling the other source must not raise the callback a second time.
        first.Signal();
        Assert.Equal(1, signaled);
    }

    [Fact]
    public void SourceRegistrations_AreReleased_WhenLastConsumerDisposes()
    {
        var first = new TrackingChangeToken();
        var second = new TrackingChangeToken();
        var token = new LinkedChangeToken([first, second]);

        var registration = token.RegisterChangeCallback(static _ => { }, null);

        Assert.Equal(1, first.OutstandingRegistrations);
        Assert.Equal(1, second.OutstandingRegistrations);

        registration.Dispose();

        // Nothing is listening any more, so the token must stop listening to its sources. This is the leak the
        // type exists to avoid: CompositeChangeToken would hold these until it signaled, which for a source
        // which never signals is never.
        Assert.Equal(0, first.OutstandingRegistrations);
        Assert.Equal(0, second.OutstandingRegistrations);
    }

    [Fact]
    public void SourceRegistrations_AreRetained_WhileAnyConsumerRemains()
    {
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);

        var first = token.RegisterChangeCallback(static _ => { }, null);
        var second = token.RegisterChangeCallback(static _ => { }, null);

        // Each consumer holds its own registration on the source.
        Assert.Equal(2, source.OutstandingRegistrations);

        first.Dispose();
        Assert.Equal(1, source.OutstandingRegistrations);

        second.Dispose();
        Assert.Equal(0, source.OutstandingRegistrations);
    }

    [Fact]
    public void Constructor_Throws_WhenSourcesIsNull()
        => Assert.Throws<ArgumentNullException>(() => new LinkedChangeToken(null!));

    [Fact]
    public void RegisterChangeCallback_Throws_WhenCallbackIsNull()
    {
        var token = new LinkedChangeToken([new TrackingChangeToken()]);

        Assert.Throws<ArgumentNullException>(() => token.RegisterChangeCallback(null!, state: null));
    }

    [Fact]
    public void SourceRegistrations_AreReleased_WhenSignaled()
    {
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);

        using var registration = token.RegisterChangeCallback(static _ => { }, null);
        Assert.Equal(1, source.OutstandingRegistrations);

        source.Signal();

        Assert.True(token.HasChanged);
        Assert.Equal(0, source.OutstandingRegistrations);
    }

    [Fact]
    public void DisposingARegistrationTwice_ReleasesItsSourcesOnce()
    {
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);

        var first = token.RegisterChangeCallback(static _ => { }, null);
        var second = token.RegisterChangeCallback(static _ => { }, null);

        first.Dispose();
        first.Dispose();

        // The second consumer is still listening, so a double dispose of the first must not have released it.
        Assert.Equal(1, source.OutstandingRegistrations);

        second.Dispose();
        Assert.Equal(0, source.OutstandingRegistrations);
    }

    [Fact]
    public void RegisteringAgain_ListensToSourcesAgain()
    {
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);

        token.RegisterChangeCallback(static _ => { }, null).Dispose();
        Assert.Equal(0, source.OutstandingRegistrations);

        var signaled = false;
        using var registration = token.RegisterChangeCallback(_ => signaled = true, null);
        Assert.Equal(1, source.OutstandingRegistrations);

        source.Signal();
        Assert.True(signaled);
    }

    [Fact]
    public void RegisteringAfterASourceHasSignaled_PropagatesWhateverTheSourceDoes()
    {
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);

        source.Signal();

        var signaled = false;
        using var registration = token.RegisterChangeCallback(_ => signaled = true, null);

        // Raising the callback when registering on a source which has already signaled is not required by
        // IChangeToken. A source backed by a CancellationToken does it, and because this token registers on its
        // sources directly rather than proxying them, that behaviour reaches the consumer unchanged.
        Assert.True(signaled);

        // HasChanged, unlike the callback, is reliable whatever the source does, because it polls.
        Assert.True(token.HasChanged);

        // The callback ran during registration, so the source registration it made was released there too.
        Assert.Equal(0, source.OutstandingRegistrations);
    }

    [Fact]
    public void HasChanged_IsTrue_WhenAPollOnlySourceChangedBeforeRegistering()
    {
        // A source which raises no callbacks cannot notify a consumer at all, so polling is the only way its
        // change is ever seen. This is the case the contract has in mind when it says callbacks are best effort.
        var passive = new PassiveChangeToken();
        var token = new LinkedChangeToken([passive, new TrackingChangeToken()]);

        passive.HasChanged = true;

        var signaled = false;
        using var registration = token.RegisterChangeCallback(_ => signaled = true, null);

        Assert.False(signaled);
        Assert.True(token.HasChanged);
    }

    [Fact]
    public void ASourceSignalingWhileLinking_DoesNotOrphanTheLaterSourceRegistrations()
    {
        var first = new TrackingChangeToken();
        var second = new TrackingChangeToken();
        var third = new TrackingChangeToken();

        // Signaled up front, so that registering on it raises the callback from inside the linking loop. The
        // release that triggers runs before the later sources have been registered on, so it cannot see them and
        // the check made once linking finishes is the only thing which can release them.
        first.Signal();

        var token = new LinkedChangeToken([first, second, third]);
        using var registration = token.RegisterChangeCallback(static _ => { }, null);

        // The later sources were registered on...
        Assert.Equal(1, second.TotalRegistrations);
        Assert.Equal(1, third.TotalRegistrations);

        // ...and none of those registrations was left behind.
        Assert.Equal(0, first.OutstandingRegistrations);
        Assert.Equal(0, second.OutstandingRegistrations);
        Assert.Equal(0, third.OutstandingRegistrations);
    }

    [Fact]
    public async Task ASourceSignalingWhileACallbackRuns_NeitherInvokesItAgainNorBlocks()
    {
        // How long a step which should complete immediately is given before it is treated as blocked. A passing
        // run never waits for it, since every wait below ends as soon as the step it waits for happens; it only
        // bounds how long a regression takes to fail.
        var blockedTimeout = TimeSpan.FromSeconds(5);

        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        // Real cancellation-backed tokens rather than the tracking fake, which is not built for concurrent use.
        var token = new LinkedChangeToken([new CancellationChangeToken(first.Token), new CancellationChangeToken(second.Token)]);

        using var callbackRunning = new ManualResetEventSlim(false);
        using var releaseCallback = new ManualResetEventSlim(false);
        var invocations = 0;

        using var registration = token.RegisterChangeCallback(
            _ =>
            {
                Interlocked.Increment(ref invocations);
                callbackRunning.Set();
                releaseCallback.Wait(blockedTimeout);
            },
            null);

        // Signalling the first source raises the callback, which parks. Waiting for it makes the overlap below a
        // fact rather than something the thread pool may or may not produce.
        var firstChange = Task.Run(first.Cancel);
        Assert.True(callbackRunning.Wait(blockedTimeout), "The callback was never raised.");

        // The second source now signals while that callback is definitely still running. It must not wait on it,
        // which it would if this token serialised callbacks behind a lock, and nothing here would release it.
        var secondChange = Task.Run(second.Cancel);
        var secondCompleted = await Task.WhenAny(secondChange, Task.Delay(blockedTimeout));
        Assert.True(secondCompleted == secondChange, "Signalling a source blocked behind a callback which was still running.");

        releaseCallback.Set();
        var firstCompleted = await Task.WhenAny(firstChange, Task.Delay(blockedTimeout));
        Assert.True(firstCompleted == firstChange, "Signalling a source did not complete once its callback returned.");

        await Task.WhenAll(firstChange, secondChange);

        Assert.Equal(1, Volatile.Read(ref invocations));
        Assert.True(token.HasChanged);
    }

    [Fact]
    public void AConsumerDisposingAfterASignalHasClaimedItsCallback_StillGetsTheStateItRegisteredWith()
    {
        // A signal has claimed the consumer's callback and not yet read the state it was registered with, and the
        // consumer disposes right then. That state belongs to the signal now, so disposal must leave it alone.
        // Disposing from OnCallbackClaimed is what puts the two paths in that order.
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);
        var expectedState = new object();

        IDisposable registration = null!;
        object? observedState = null;
        var invocations = 0;
        var forced = 0;

        token.OnCallbackClaimed = () =>
        {
            forced++;
            registration.Dispose();
        };

        registration = token.RegisterChangeCallback(
            state =>
            {
                invocations++;
                observedState = state;
            },
            expectedState);

        source.Signal();

        // The interleaving really was forced, rather than the test having quietly asserted nothing.
        Assert.Equal(1, forced);

        Assert.Equal(1, invocations);
        Assert.Same(expectedState, observedState);

        // Disposal losing the callback does not stop it from releasing what it holds on the sources.
        Assert.Equal(0, source.OutstandingRegistrations);
    }

    [Fact]
    public void ASignalAfterTheConsumerHasDisposed_DoesNotReachIt()
    {
        // The other order: disposal claimed the callback first, so the signal has nothing left to raise.
        var source = new TrackingChangeToken();
        var token = new LinkedChangeToken([source]);

        var invocations = 0;
        var registration = token.RegisterChangeCallback(_ => invocations++, new object());

        registration.Dispose();
        source.Signal();

        Assert.Equal(0, invocations);
        Assert.Equal(0, source.OutstandingRegistrations);
        Assert.True(token.HasChanged);
    }

    [Theory]
    [InlineData(1)] // One token is returned by the builder as-is, with no linking involved.
    [InlineData(2)] // Two or more are linked.
    public async Task WatcherLifecycles_DoNotAccumulateRegistrationsOnProviderChangeTokens(int changeTokenCount)
    {
        // Regression test for https://github.com/dotnet/extensions/issues/7673: a watcher is created and disposed
        // every time the resolver evicts an idle service name, and each lifecycle used to add a registration on
        // the provider's change token which was never released.
        var sources = Enumerable.Range(0, changeTokenCount).Select(_ => new TrackingChangeToken()).ToArray();
        var provider = new FakeEndpointProvider(builder =>
        {
            foreach (var source in sources)
            {
                builder.AddChangeToken(source);
            }

            builder.Endpoints.Add(ServiceEndpoint.Create(new IPEndPoint(IPAddress.Loopback, 8080)));
        });

        var services = new ServiceCollection()
            .AddSingleton<IServiceEndpointProviderFactory>(new FakeEndpointProviderFactory(provider))
            .AddServiceDiscoveryCore()
            .BuildServiceProvider();
        var watcherFactory = services.GetRequiredService<ServiceEndpointWatcherFactory>();

        const int Lifecycles = 5;
        for (var i = 0; i < Lifecycles; i++)
        {
            ServiceEndpointWatcher watcher;
            await using ((watcher = watcherFactory.CreateWatcher("http://basket")).ConfigureAwait(false))
            {
                var endpoints = await watcher.GetEndpointsAsync(CancellationToken.None);
                Assert.Single(endpoints.Endpoints);
            }
        }

        foreach (var source in sources)
        {
            // Sanity check that the watcher did register, so that the assertion below is meaningful.
            Assert.Equal(Lifecycles, source.TotalRegistrations);
            Assert.Equal(0, source.OutstandingRegistrations);
        }
    }

    private sealed class FakeEndpointProviderFactory(IServiceEndpointProvider provider) : IServiceEndpointProviderFactory
    {
        public bool TryCreateProvider(ServiceEndpointQuery query, [NotNullWhen(true)] out IServiceEndpointProvider? resolver)
        {
            resolver = provider;
            return true;
        }
    }

    private sealed class FakeEndpointProvider(Action<IServiceEndpointBuilder> populate) : IServiceEndpointProvider
    {
        public ValueTask PopulateAsync(IServiceEndpointBuilder endpoints, CancellationToken cancellationToken)
        {
            populate(endpoints);
            return default;
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>
    /// A change token which raises callbacks and keeps count of the registrations which have not been released.
    /// </summary>
    private sealed class TrackingChangeToken : IChangeToken
    {
        private readonly CancellationTokenSource _cts = new();

        public bool ActiveChangeCallbacks => true;

        public bool HasChanged => _cts.IsCancellationRequested;

        /// <summary>Gets the number of registrations which have been made and not yet released.</summary>
        public int OutstandingRegistrations { get; private set; }

        /// <summary>Gets the number of registrations which have been made.</summary>
        public int TotalRegistrations { get; private set; }

        public void Signal() => _cts.Cancel();

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
        {
            OutstandingRegistrations++;
            TotalRegistrations++;
            return new Registration(this, _cts.Token.Register(callback, state));
        }

        private sealed class Registration(TrackingChangeToken owner, CancellationTokenRegistration registration) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                registration.Dispose();
                owner.OutstandingRegistrations--;
            }
        }
    }

    /// <summary>
    /// A change token whose changes are observable only by polling <see cref="IChangeToken.HasChanged"/>.
    /// </summary>
    private sealed class PassiveChangeToken : IChangeToken
    {
        public bool ActiveChangeCallbacks => false;

        public bool HasChanged { get; set; }

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
            => throw new InvalidOperationException("This token does not raise callbacks and must not be registered on.");
    }
}
