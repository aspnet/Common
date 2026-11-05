// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class FailoverChatClientTests
{
    [Fact]
    public void MaximumAttemptsPerRequest_ValidatesValue()
    {
        using var client = new DelegatingFailoverTestRouter(
            _ => throw new NotSupportedException());

        Assert.Null(client.MaximumAttemptsPerRequest);
        client.MaximumAttemptsPerRequest = 2;
        Assert.Equal(2, client.MaximumAttemptsPerRequest);
        client.MaximumAttemptsPerRequest = null;
        Assert.Null(client.MaximumAttemptsPerRequest);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.MaximumAttemptsPerRequest = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.MaximumAttemptsPerRequest = -1);
    }

    [Fact]
    public async Task Failover_RejectsNullMessagesForNonStreamingAndStreaming()
    {
        using var selected = new TestChatClient();
        using var router = new DelegatingFailoverTestRouter(_ => selected);

        await Assert.ThrowsAsync<ArgumentNullException>(() => router.GetResponseAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CollectAsync(router.GetStreamingResponseAsync(null!)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialSelectionFailureDoesNotReportAttempt(bool streaming)
    {
        var expected = new InvalidOperationException("selection failed");
        RoutingContext? selectedContext = null;
        int updateCount = 0;
        using var router = new DelegatingFailoverTestRouter(
            context =>
            {
                selectedContext = context;
                throw expected;
            },
            (_, _, _) => updateCount++);

        Task<ChatResponse> operation = streaming
            ? router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync()
            : router.GetResponseAsync([new(ChatRole.User, "hi")]);
        InvalidOperationException actual =
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);

        Assert.Same(expected, actual);
        Assert.NotNull(selectedContext);
        Assert.Equal(0, updateCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialSelectionFailureWithCallerCancellationPropagatesSelectionFailure(bool streaming)
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var selectionException = new InvalidOperationException("selection failed");
        int updateCount = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => throw selectionException,
            (_, _, _) => updateCount++);

        Task<ChatResponse> operation = streaming
            ? router.GetStreamingResponseAsync(
                [new(ChatRole.User, "hi")],
                cancellationToken: cancellationSource.Token).ToChatResponseAsync()
            : router.GetResponseAsync(
                [new(ChatRole.User, "hi")],
                cancellationToken: cancellationSource.Token);
        InvalidOperationException actual =
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);

        Assert.Same(selectionException, actual);
        Assert.Equal(0, updateCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullSelectionDoesNotReportAttempt(bool streaming)
    {
        int updateCount = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => null!,
            (_, _, _) => updateCount++);

        Task<ChatResponse> operation = streaming
            ? router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync()
            : router.GetResponseAsync([new(ChatRole.User, "hi")]);
        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);

        Assert.Contains("SelectClientAsync", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, updateCount);
    }

    [Fact]
    public async Task Failover_RetrySelectionFailureReportsOnlyCompletedAttempt()
    {
        var invocationException = new InvalidOperationException("invocation failed");
        var selectionException = new InvalidOperationException("selection failed");
        using var failing = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw invocationException,
        };
        int selections = 0;
        var updates = new List<(FailoverChatClientAttempt attempt, bool isTerminal)>();
        using var router = new DelegatingFailoverTestRouter(
            _ => ++selections == 1 ? failing : throw selectionException,
            (_, attempt, isTerminal) =>
            {
                updates.Add((attempt, isTerminal));
            });

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Same(selectionException, actual);
        Assert.Equal(2, selections);
        (FailoverChatClientAttempt attempt, bool isTerminal) update = Assert.Single(updates);
        Assert.Same(failing, update.attempt.Client);
        Assert.Same(invocationException, update.attempt.Exception);
        Assert.False(update.isTerminal);
    }

    [Fact]
    public async Task TerminalRoutingUpdateFailureReplacesRequestFailure()
    {
        var invocationException = new InvalidOperationException("invocation failed");
        var completionException = new InvalidOperationException("completion failed");
        using var failing = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw invocationException,
        };
        using var router = new DelegatingFailoverTestRouter(
            _ => failing,
            (_, _, isTerminal) =>
            {
                Assert.True(isTerminal);
                throw completionException;
            })
        {
            MaximumAttemptsPerRequest = 1,
        };

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Same(completionException, actual);
    }

    [Fact]
    public async Task NonterminalRoutingUpdateFailureStopsWithoutAnotherUpdate()
    {
        var invocationException = new InvalidOperationException("invocation failed");
        var updateException = new InvalidOperationException("update failed");
        using var failing = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw invocationException,
        };
        int selections = 0;
        int updates = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return failing;
            },
            (_, attempt, isTerminal) =>
            {
                updates++;
                Assert.Same(invocationException, attempt!.Exception);
                Assert.False(isTerminal);
                throw updateException;
            });

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Same(updateException, actual);
        Assert.Equal(1, selections);
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task Dispatch_ConfiguredClientPreservesAndOverridesRequestOptions()
    {
        ChatOptions? forwarded = null;
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, options, _) =>
            {
                forwarded = options;
                return Task.FromResult(new ChatResponse());
            },
        };
        using var configured = new ConfigureOptionsChatClient(
            inner,
            options => options.ModelId = "route");
        var requestOptions = new ChatOptions
        {
            Instructions = "caller",
            ModelId = "request",
        };
        using var router = new DelegatingTestRouter(_ => configured);

        _ = await router.GetResponseAsync([new(ChatRole.User, "hi")], requestOptions);

        Assert.NotSame(requestOptions, forwarded);
        Assert.Equal("route", forwarded!.ModelId);
        Assert.Equal("caller", forwarded.Instructions);
        Assert.Equal("request", requestOptions.ModelId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failover_UsesRequestOptionsForEveryAttempt(bool streaming)
    {
        var requestOptions = new ChatOptions
        {
            Instructions = "caller",
            ModelId = "request",
        };
        var invokedOptions = new List<ChatOptions>();
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, options, _) =>
            {
                invokedOptions.Add(options!);
                throw new InvalidOperationException("failed");
            },
            GetStreamingResponseAsyncCallback = (_, options, _) =>
            {
                invokedOptions.Add(options!);
                return ThrowingStream("failed");
            },
        };
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, options, _) =>
            {
                invokedOptions.Add(options!);
                return Task.FromResult(expected);
            },
            GetStreamingResponseAsyncCallback = (_, options, _) =>
            {
                invokedOptions.Add(options!);
                return YieldUpdates("ok");
            },
        };
        int selections = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => ++selections == 1 ? first : second);

        ChatResponse response = streaming
            ? await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")], requestOptions).ToChatResponseAsync()
            : await router.GetResponseAsync([new(ChatRole.User, "hi")], requestOptions);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, selections);
        Assert.Equal(2, invokedOptions.Count);

        // Every attempt receives the request's options, which are a clone of the caller's instance.
        Assert.Same(invokedOptions[0], invokedOptions[1]);
        Assert.NotSame(requestOptions, invokedOptions[0]);
        Assert.Equal("request", invokedOptions[0].ModelId);
        Assert.Equal("caller", invokedOptions[0].Instructions);
        Assert.Equal("request", requestOptions.ModelId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failover_SelectionAndInvocationShareRequestOptions(bool streaming)
    {
        var requestOptions = new ChatOptions { ModelId = "request" };
        var invokedOptions = new List<ChatOptions?>();
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, options, _) =>
            {
                invokedOptions.Add(options);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            },
            GetStreamingResponseAsyncCallback = (_, options, _) =>
            {
                invokedOptions.Add(options);
                return YieldUpdates("ok");
            },
        };
        ChatOptions? selectedOptions = null;
        using var router = new DelegatingFailoverTestRouter(
            context =>
            {
                selectedOptions = context.ChatOptions;

                // Mutating the request's options during selection shapes the request.
                context.ChatOptions!.Temperature = 0.25f;
                return inner;
            });

        _ = streaming
            ? await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")], requestOptions).ToChatResponseAsync()
            : await router.GetResponseAsync([new(ChatRole.User, "hi")], requestOptions);

        Assert.Same(selectedOptions, invokedOptions[0]);
        Assert.Equal(0.25f, invokedOptions[0]!.Temperature);
        Assert.Null(requestOptions.Temperature);
    }

    [Fact]
    public async Task Failure_Propagates()
    {
        var expected = new InvalidOperationException("failed");
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw expected,
        };
        FailoverChatClientAttempt? observed = null;
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                observed = attempt;
            })
        {
            MaximumAttemptsPerRequest = 1,
        };

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Same(expected, actual);
        Assert.Same(inner, observed!.Client);
        Assert.Same(expected, observed.Exception);
        Assert.False(observed.ResponseCompleted);
    }

    [Fact]
    public async Task Failover_UpdateChangesStateBeforeNextSelection()
    {
        using var failing = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw new InvalidOperationException("failed"),
        };
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var working = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(expected),
        };
        RoutingContext? initialContext = null;
        FailoverChatClientAttempt? failedAttempt = null;
        FailoverChatClientAttempt? terminalAttempt = null;
        using var router = new DelegatingFailoverTestRouter(
            context =>
            {
                initialContext ??= context;
                Assert.Same(initialContext, context);
                return failedAttempt is null ? failing : working;
            },
            (context, attempt, isTerminal) =>
            {
                Assert.Same(initialContext, context);
                if (isTerminal)
                {
                    terminalAttempt = attempt;
                }
                else
                {
                    failedAttempt = attempt;
                }
            });

        ChatResponse response = await router.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
        Assert.Same(failing, failedAttempt!.Client);
        Assert.Equal("failed", Assert.IsType<InvalidOperationException>(failedAttempt.Exception).Message);
        Assert.True(failedAttempt.Duration >= TimeSpan.Zero);
        Assert.Null(failedAttempt.TimeToFirstUpdate);
        Assert.False(failedAttempt.OutputCommitted);
        Assert.False(failedAttempt.ResponseCompleted);
        Assert.Same(working, terminalAttempt!.Client);
        Assert.Null(terminalAttempt.Exception);
        Assert.True(terminalAttempt.ResponseCompleted);
    }

    [Fact]
    public async Task Failover_CanRetrySameClient()
    {
        int calls = 0;
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                ++calls == 1
                    ? throw new InvalidOperationException("transient")
                    : Task.FromResult(new ChatResponse()),
        };
        using var router = new DelegatingFailoverTestRouter(
            _ => inner);

        _ = await router.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MaximumAttemptsPerRequest_AllowsSuccessAtLimit()
    {
        int calls = 0;
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                ++calls == 1
                    ? throw new InvalidOperationException("transient")
                    : Task.FromResult(expected),
        };
        using var router = new DelegatingFailoverTestRouter(
            _ => inner)
        {
            MaximumAttemptsPerRequest = 2,
        };

        ChatResponse response = await router.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MaximumAttemptsPerRequest_RethrowsLastFailure()
    {
        int calls = 0;
        var terminalUpdates = new List<bool>();
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                throw new InvalidOperationException($"failure {++calls}"),
        };
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                Assert.NotNull(attempt);
                terminalUpdates.Add(isTerminal);
            })
        {
            MaximumAttemptsPerRequest = 2,
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Equal("failure 2", exception.Message);
        Assert.Equal(2, calls);
        Assert.Equal([false, true], terminalUpdates);
    }

    [Fact]
    public async Task Failover_NullResponseIsReturnedWithoutFailover()
    {
        using var nullClient = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult<ChatResponse>(null!),
        };
        int selections = 0;
        FailoverChatClientAttempt? terminalAttempt = null;
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return nullClient;
            },
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                terminalAttempt = attempt;
            });

        ChatResponse? response = await router.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Null(response);
        Assert.Equal(1, selections);
        Assert.Null(terminalAttempt!.Exception);
        Assert.True(terminalAttempt!.ResponseCompleted);
    }

    [Fact]
    public async Task Cancellation_DoesNotReselect()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        int selections = 0;
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, cancellationToken) =>
                throw new OperationCanceledException(cancellationToken),
        };
        using var router = new DelegatingTestRouter(_ =>
        {
            selections++;
            return inner;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.GetResponseAsync(
                [new(ChatRole.User, "hi")],
                cancellationToken: cancellationSource.Token));

        Assert.Equal(1, selections);
    }

    [Fact]
    public async Task Failover_CancellationReportsTerminalUpdate()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var failure = new InvalidOperationException("failed");
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw failure,
        };
        int updateCount = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                updateCount++;
                Assert.Same(failure, attempt!.Exception);
                Assert.True(isTerminal);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.GetResponseAsync(
                [new(ChatRole.User, "hi")],
                cancellationToken: cancellationSource.Token));

        Assert.Equal(1, updateCount);
    }

    [Fact]
    public async Task Failover_CancellationDuringUpdateIsObservedByNextAttempt()
    {
        using var cancellationSource = new CancellationTokenSource();
        using var inner = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw new InvalidOperationException("failed"),
        };
        int selections = 0;
        var updates = new List<(FailoverChatClientAttempt attempt, bool isTerminal)>();
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return inner;
            },
            (_, attempt, isTerminal) =>
            {
                updates.Add((attempt, isTerminal));
                if (!isTerminal)
                {
                    cancellationSource.Cancel();
                }
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.GetResponseAsync(
                [new(ChatRole.User, "hi")],
                cancellationToken: cancellationSource.Token));

        Assert.Equal(2, selections);
        Assert.Collection(
            updates,
            update =>
            {
                Assert.NotNull(update.attempt);
                Assert.False(update.isTerminal);
            },
            update =>
            {
                Assert.NotNull(update.attempt);
                Assert.True(update.isTerminal);
            });
    }

    [Fact]
    public async Task Streaming_PreOutputFailurePropagates()
    {
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream("failed"),
        };
        FailoverChatClientAttempt? observed = null;
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                observed = attempt;
            })
        {
            MaximumAttemptsPerRequest = 1,
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(router.GetStreamingResponseAsync([new(ChatRole.User, "hi")])));

        Assert.Equal("failed", exception.Message);
        Assert.Same(exception, observed!.Exception);
        Assert.False(observed.OutputCommitted);
        Assert.False(observed.ResponseCompleted);
    }

    [Fact]
    public async Task Streaming_FallsBackBeforeFirstUpdate()
    {
        using var failing = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream("failed"),
        };
        using var working = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("ok"),
        };
        int selections = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => ++selections == 1 ? failing : working);

        ChatResponse response =
            await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, selections);
    }

    [Fact]
    public async Task Streaming_CancellationDuringUpdateIsObservedByNextAttempt()
    {
        using var cancellationSource = new CancellationTokenSource();
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream("failed"),
        };
        int selections = 0;
        var updates = new List<(FailoverChatClientAttempt attempt, bool isTerminal)>();
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return inner;
            },
            (_, attempt, isTerminal) =>
            {
                updates.Add((attempt, isTerminal));
                if (!isTerminal)
                {
                    cancellationSource.Cancel();
                }
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(
                router.GetStreamingResponseAsync(
                    [new(ChatRole.User, "hi")],
                    cancellationToken: cancellationSource.Token)));

        Assert.Equal(2, selections);
        Assert.Collection(
            updates,
            update =>
            {
                Assert.NotNull(update.attempt);
                Assert.False(update.isTerminal);
            },
            update =>
            {
                Assert.NotNull(update.attempt);
                Assert.True(update.isTerminal);
            });
    }

    [Fact]
    public async Task Streaming_CancellationDoesNotSelectNext()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream("failed"),
        };
        int selections = 0;
        FailoverChatClientAttempt? observed = null;
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return inner;
            },
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                observed = attempt;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(
                router.GetStreamingResponseAsync(
                    [new(ChatRole.User, "hi")],
                    cancellationToken: cancellationSource.Token)));

        Assert.Equal(1, selections);
        Assert.IsType<InvalidOperationException>(observed!.Exception);
        Assert.False(observed.OutputCommitted);
        Assert.False(observed.ResponseCompleted);
    }

    [Fact]
    public async Task OperationCanceledException_CanTriggerFailover()
    {
        bool secondCalled = false;
        ChatResponse expected = new(new ChatMessage(ChatRole.Assistant, "ok"));
        using var first = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => throw new OperationCanceledException(),
        };
        using var second = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                secondCalled = true;
                return Task.FromResult(expected);
            },
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        ChatResponse response = await client.GetResponseAsync([new(ChatRole.User, "hi")]);

        Assert.Same(expected, response);
        Assert.True(secondCalled);
    }

    [Fact]
    public async Task Streaming_OperationCanceledExceptionCanTriggerFailover()
    {
        bool secondCalled = false;
        using var first = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => CanceledStream(CancellationToken.None),
        };
        using var second = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) =>
            {
                secondCalled = true;
                return YieldUpdates("ok");
            },
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        ChatResponse response =
            await client.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.True(secondCalled);
    }

    [Fact]
    public async Task Streaming_DisposalCancellationCanTriggerFailover()
    {
        bool secondCalled = false;
        var canceledStream = new TrackingAsyncEnumerable(
            [],
            disposeException: new OperationCanceledException());
        using var first = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => canceledStream,
        };
        using var second = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) =>
            {
                secondCalled = true;
                return YieldUpdates("ok");
            },
        };
        using var client = new OrderedFailoverChatClient([first, second]);

        ChatResponse response =
            await client.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.True(secondCalled);
        Assert.Equal(1, canceledStream.DisposeCount);
    }

    [Fact]
    public async Task Streaming_MaximumAttemptsPerRequest_RethrowsLastFailure()
    {
        int calls = 0;
        var terminalUpdates = new List<bool>();
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => ThrowingStream($"failure {++calls}"),
        };
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                Assert.NotNull(attempt);
                terminalUpdates.Add(isTerminal);
            })
        {
            MaximumAttemptsPerRequest = 2,
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(router.GetStreamingResponseAsync([new(ChatRole.User, "hi")])));

        Assert.Equal("failure 2", exception.Message);
        Assert.Equal(2, calls);
        Assert.Equal([false, true], terminalUpdates);
    }

    [Fact]
    public async Task Streaming_StreamCreationFailureFallsBack()
    {
        using var failing = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => throw new InvalidOperationException("failed"),
        };
        using var working = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("ok"),
        };
        int selections = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => ++selections == 1 ? failing : working);

        ChatResponse response =
            await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, selections);
    }

    [Fact]
    public async Task Streaming_EnumeratorCreationFailureFallsBack()
    {
        using var failing = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) =>
                new ThrowingGetAsyncEnumeratorEnumerable(new InvalidOperationException("failed")),
        };
        using var working = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("ok"),
        };
        int selections = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => ++selections == 1 ? failing : working);

        ChatResponse response =
            await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, selections);
    }

    [Fact]
    public async Task Streaming_CurrentFailureFallsBackBeforeOutput()
    {
        var currentException = new InvalidOperationException("current failed");
        var failedStream = new ThrowingCurrentAsyncEnumerable(currentException);
        using var failing = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => failedStream,
        };
        using var working = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("ok"),
        };
        FailoverChatClientAttempt? failedAttempt = null;
        using var router = new DelegatingFailoverTestRouter(
            _ => failedAttempt is null ? failing : working,
            (_, attempt, isTerminal) =>
            {
                if (!isTerminal)
                {
                    failedAttempt = attempt;
                }
            });

        ChatResponse response =
            await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.Same(currentException, failedAttempt!.Exception);
        Assert.False(failedAttempt.OutputCommitted);
        Assert.False(failedAttempt.ResponseCompleted);
        Assert.Null(failedAttempt.TimeToFirstUpdate);
        Assert.Equal(1, failedStream.DisposeCount);
    }

    [Fact]
    public async Task Streaming_CurrentFailureCancellationIsObservedByNextAttempt()
    {
        using var cancellationSource = new CancellationTokenSource();
        var failedStream = new ThrowingCurrentAsyncEnumerable(new InvalidOperationException("current failed"));
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => failedStream,
        };
        int selections = 0;
        var updates = new List<(FailoverChatClientAttempt attempt, bool isTerminal)>();
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return inner;
            },
            (_, attempt, isTerminal) =>
            {
                updates.Add((attempt, isTerminal));
                if (!isTerminal)
                {
                    cancellationSource.Cancel();
                }
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(
                router.GetStreamingResponseAsync(
                    [new(ChatRole.User, "hi")],
                    cancellationToken: cancellationSource.Token)));

        Assert.Equal(2, selections);
        Assert.Collection(
            updates,
            update =>
            {
                Assert.NotNull(update.attempt);
                Assert.False(update.isTerminal);
            },
            update =>
            {
                Assert.NotNull(update.attempt);
                Assert.True(update.isTerminal);
            });
        Assert.Equal(2, failedStream.DisposeCount);
    }

    [Fact]
    public async Task Streaming_EmptyStreamDisposalFailureFallsBack()
    {
        var disposalException = new InvalidOperationException("dispose failed");
        var failedStream = new TrackingAsyncEnumerable([], disposeException: disposalException);
        using var failing = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => failedStream,
        };
        using var working = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("ok"),
        };
        FailoverChatClientAttempt? failedAttempt = null;
        FailoverChatClientAttempt? terminalAttempt = null;
        using var router = new DelegatingFailoverTestRouter(
            _ => failedAttempt is null ? failing : working,
            (_, attempt, isTerminal) =>
            {
                if (isTerminal)
                {
                    terminalAttempt = attempt;
                }
                else
                {
                    failedAttempt = attempt;
                }
            });

        ChatResponse response =
            await router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync();

        Assert.Equal("ok", response.Text);
        Assert.Same(disposalException, failedAttempt!.Exception);
        Assert.False(failedAttempt.OutputCommitted);
        Assert.False(failedAttempt.ResponseCompleted);
        Assert.True(terminalAttempt!.ResponseCompleted);
        Assert.Equal(1, failedStream.DisposeCount);
    }

    [Fact]
    public async Task Streaming_MidStreamFailureIsObservedAndDoesNotReselect()
    {
        var stream = new TrackingAsyncEnumerable(
            [new ChatResponseUpdate(ChatRole.Assistant, "first")],
            throwOnMove: 2,
            exception: new InvalidOperationException("mid-stream"));
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => stream,
        };
        int selections = 0;
        FailoverChatClientAttempt? observed = null;
        using var router = new DelegatingFailoverTestRouter(
            _ =>
            {
                selections++;
                return inner;
            },
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                observed = attempt;
            });
        var updates = new List<ChatResponseUpdate>();

        async Task ConsumeAsync()
        {
            await foreach (ChatResponseUpdate update in router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]))
            {
                updates.Add(update);
            }
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(ConsumeAsync);

        Assert.Equal("mid-stream", exception.Message);
        Assert.Equal("first", Assert.Single(updates).Text);
        Assert.Equal(1, selections);
        Assert.Same(exception, observed!.Exception);
        Assert.True(observed.OutputCommitted);
        Assert.False(observed.ResponseCompleted);
        Assert.NotNull(observed.TimeToFirstUpdate);
        Assert.True(observed.Duration >= observed.TimeToFirstUpdate.Value);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Streaming_CompletionNotifiesHook()
    {
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("a", "b"),
        };
        int completions = 0;
        FailoverChatClientAttempt? observed = null;
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                completions++;
                observed = attempt;
            });

        List<ChatResponseUpdate> updates =
            await CollectAsync(router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Equal(2, updates.Count);
        Assert.Equal(1, completions);
        Assert.Null(observed!.Exception);
        Assert.True(observed.OutputCommitted);
        Assert.True(observed.ResponseCompleted);
        Assert.NotNull(observed.TimeToFirstUpdate);
    }

    [Fact]
    public async Task Streaming_EmptyCompletionNotifiesHook()
    {
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates(),
        };
        int updateCount = 0;
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                updateCount++;
                Assert.NotNull(attempt);
                Assert.Null(attempt.Exception);
                Assert.False(attempt.OutputCommitted);
                Assert.True(attempt.ResponseCompleted);
                Assert.True(isTerminal);
            });

        List<ChatResponseUpdate> updates =
            await CollectAsync(router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Empty(updates);
        Assert.Equal(1, updateCount);
    }

    [Fact]
    public async Task Streaming_CallerStopsEarlyNotifiesHookAsIncomplete()
    {
        using var inner = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) => YieldUpdates("a", "b"),
        };
        int completions = 0;
        FailoverChatClientAttempt? observed = null;
        using var router = new DelegatingFailoverTestRouter(
            _ => inner,
            (_, attempt, isTerminal) =>
            {
                Assert.True(isTerminal);
                completions++;
                observed = attempt;
            });

        await ConsumeOneAsync(router.GetStreamingResponseAsync([new(ChatRole.User, "hi")]));

        Assert.Equal(1, completions);
        Assert.Null(observed!.Exception);
        Assert.True(observed.OutputCommitted);
        Assert.False(observed.ResponseCompleted);
    }

    private static async Task<List<ChatResponseUpdate>> CollectAsync(
        IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var result = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in updates)
        {
            result.Add(update);
        }

        return result;
    }

    private static async Task ConsumeOneAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = updates.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
    }

    internal static async IAsyncEnumerable<ChatResponseUpdate> YieldUpdates(params string[] texts)
    {
        foreach (string text in texts)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }
    }

    internal static async IAsyncEnumerable<ChatResponseUpdate> ThrowingStream(string message)
    {
        await Task.Yield();
        foreach (int _ in Array.Empty<int>())
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "never");
        }

        throw new InvalidOperationException(message);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CanceledStream(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (int _ in Array.Empty<int>())
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "never");
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private sealed class DelegatingTestRouter : RoutingChatClient
    {
        private readonly Func<RoutingContext, IChatClient> _select;

        public DelegatingTestRouter(Func<RoutingContext, IChatClient> select)
        {
            _select = select;
        }

        protected override ValueTask<IChatClient> SelectClientAsync(
            RoutingContext context,
            CancellationToken cancellationToken) =>
            new(_select(context));
    }

    private sealed class DelegatingFailoverTestRouter : FailoverChatClient
    {
        private readonly Func<RoutingContext, IChatClient> _select;
        private readonly Action<RoutingContext, FailoverChatClientAttempt, bool>? _onRoutingUpdate;

        public DelegatingFailoverTestRouter(
            Func<RoutingContext, IChatClient> select,
            Action<RoutingContext, FailoverChatClientAttempt, bool>? onRoutingUpdate = null)
        {
            _select = select;
            _onRoutingUpdate = onRoutingUpdate;
        }

        protected override ValueTask<IChatClient> SelectClientAsync(
            RoutingContext context,
            CancellationToken cancellationToken) =>
            new(_select(context));

        protected override ValueTask OnRoutingUpdateAsync(
            RoutingContext context,
            FailoverChatClientAttempt attempt,
            bool isTerminal,
            CancellationToken cancellationToken)
        {
            _onRoutingUpdate?.Invoke(context, attempt, isTerminal);
            return default;
        }
    }

    private sealed class ThrowingGetAsyncEnumeratorEnumerable : IAsyncEnumerable<ChatResponseUpdate>
    {
        private readonly Exception _exception;

        public ThrowingGetAsyncEnumeratorEnumerable(Exception exception)
        {
            _exception = exception;
        }

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) =>
            throw _exception;
    }

    private sealed class ThrowingCurrentAsyncEnumerable(Exception exception) : IAsyncEnumerable<ChatResponseUpdate>
    {
        public int DisposeCount { get; private set; }

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) =>
            new Enumerator(this, exception);

        private sealed class Enumerator(
            ThrowingCurrentAsyncEnumerable owner,
            Exception exception) : IAsyncEnumerator<ChatResponseUpdate>
        {
            public ChatResponseUpdate Current => throw exception;

            public ValueTask DisposeAsync()
            {
                owner.DisposeCount++;
                return default;
            }

            public ValueTask<bool> MoveNextAsync() => new(true);
        }
    }

    private sealed class TrackingAsyncEnumerable : IAsyncEnumerable<ChatResponseUpdate>
    {
        private readonly IReadOnlyList<ChatResponseUpdate> _updates;
        private readonly int? _throwOnMove;
        private readonly Exception? _exception;
        private readonly Exception? _disposeException;

        public TrackingAsyncEnumerable(
            IReadOnlyList<ChatResponseUpdate> updates,
            int? throwOnMove = null,
            Exception? exception = null,
            Exception? disposeException = null)
        {
            _updates = updates;
            _throwOnMove = throwOnMove;
            _exception = exception;
            _disposeException = disposeException;
        }

        public int DisposeCount { get; private set; }

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) =>
            new Enumerator(this);

        private sealed class Enumerator : IAsyncEnumerator<ChatResponseUpdate>
        {
            private readonly TrackingAsyncEnumerable _owner;
            private int _moveCount;

            public Enumerator(TrackingAsyncEnumerable owner)
            {
                _owner = owner;
            }

            public ChatResponseUpdate Current { get; private set; } = null!;

            public ValueTask<bool> MoveNextAsync()
            {
                _moveCount++;
                if (_moveCount == _owner._throwOnMove)
                {
                    throw _owner._exception!;
                }

                int index = _moveCount - 1;
                if (index >= _owner._updates.Count)
                {
                    return new(false);
                }

                Current = _owner._updates[index];
                return new(true);
            }

            public ValueTask DisposeAsync()
            {
                _owner.DisposeCount++;
                if (_owner._disposeException is { } exception)
                {
                    throw exception;
                }

                return default;
            }
        }
    }
}
