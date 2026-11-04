// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OpenTelemetry.Trace;
using Xunit;

#pragma warning disable SA1118 // Parameter should not span multiple lines
#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace Microsoft.Extensions.AI;

public class FunctionInvokingChatClientTests
{
    [Fact]
    public void InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerClient", () => new FunctionInvokingChatClient(null!));
        Assert.Throws<ArgumentNullException>("builder", () => ((ChatClientBuilder)null!).UseFunctionInvocation());
    }

    [Fact]
    public void Ctor_HasExpectedDefaults()
    {
        using TestChatClient innerClient = new();
        using FunctionInvokingChatClient client = new(innerClient);

        Assert.False(client.AllowConcurrentInvocation);
        Assert.False(client.IncludeDetailedErrors);
        Assert.Equal(40, client.MaximumIterationsPerRequest);
        Assert.Equal(3, client.MaximumConsecutiveErrorsPerRequest);
        Assert.Null(client.FunctionInvoker);
        Assert.Null(client.AdditionalTools);
    }

    [Fact]
    public void Properties_Roundtrip()
    {
        using TestChatClient innerClient = new();
        using FunctionInvokingChatClient client = new(innerClient);

        Assert.False(client.AllowConcurrentInvocation);
        client.AllowConcurrentInvocation = true;
        Assert.True(client.AllowConcurrentInvocation);

        Assert.False(client.IncludeDetailedErrors);
        client.IncludeDetailedErrors = true;
        Assert.True(client.IncludeDetailedErrors);

        Assert.Equal(40, client.MaximumIterationsPerRequest);
        client.MaximumIterationsPerRequest = 5;
        Assert.Equal(5, client.MaximumIterationsPerRequest);

        Assert.Equal(3, client.MaximumConsecutiveErrorsPerRequest);
        client.MaximumConsecutiveErrorsPerRequest = 1;
        Assert.Equal(1, client.MaximumConsecutiveErrorsPerRequest);

        Assert.Null(client.FunctionInvoker);
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> invoker = (ctx, ct) => new ValueTask<object?>("test");
        client.FunctionInvoker = invoker;
        Assert.Same(invoker, client.FunctionInvoker);

        Assert.Null(client.AdditionalTools);
        IList<AITool> additionalTools = [AIFunctionFactory.Create(() => "Additional Tool")];
        client.AdditionalTools = additionalTools;
        Assert.Same(additionalTools, client.AdditionalTools);
    }

    [Fact]
    public async Task SupportsSingleFunctionCallPerRequestAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
                AIFunctionFactory.Create((int i) => { }, "VoidReturn"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "VoidReturn", arguments: new Dictionary<string, object?> { { "i", 43 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Success: Function completed.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, plan);

        await InvokeAndAssertStreamingAsync(options, plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupportsToolsProvidedByAdditionalTools(bool provideOptions)
    {
        ChatOptions? options = provideOptions ?
            new() { Tools = [AIFunctionFactory.Create(() => "Shouldn't be invoked", "ChatOptionsFunc")] } :
            null;

        Func<ChatClientBuilder, ChatClientBuilder> configure = builder =>
            builder.UseFunctionInvocation(configure: c => c.AdditionalTools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
                AIFunctionFactory.Create((int i) => { }, "VoidReturn"),
            ]);

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "VoidReturn", arguments: new Dictionary<string, object?> { { "i", 43 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Success: Function completed.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, plan, configurePipeline: configure);

        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
    }

    [Fact]
    public async Task PrefersToolsProvidedByChatOptions()
    {
        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = builder =>
            builder.UseFunctionInvocation(configure: c => c.AdditionalTools =
            [
                AIFunctionFactory.Create(() => "Should never be invoked", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
                AIFunctionFactory.Create((int i) => { }, "VoidReturn"),
            ]);

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "VoidReturn", arguments: new Dictionary<string, object?> { { "i", 43 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Success: Function completed.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, plan, configurePipeline: configure);

        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupportsMultipleFunctionCallsPerRequestAsync(bool concurrentInvocation)
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create((int? i = 42) => "Result 1", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func1"),
                new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 34 } }),
                new FunctionCallContent("callId3", "Func2", arguments: new Dictionary<string, object?> { { "i", 56 } }),
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Result 1"),
                new FunctionResultContent("callId2", result: "Result 2: 34"),
                new FunctionResultContent("callId3", result: "Result 2: 56"),
            ]),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId4", "Func2", arguments: new Dictionary<string, object?> { { "i", 78 } }),
                new FunctionCallContent("callId5", "Func1")
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId4", result: "Result 2: 78"),
                new FunctionResultContent("callId5", result: "Result 1")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s) { AllowConcurrentInvocation = concurrentInvocation });

        await InvokeAndAssertAsync(options, plan, configurePipeline: configure);

        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
    }

    [Fact]
    public async Task ParallelFunctionCallsMayBeInvokedConcurrentlyAsync()
    {
        int remaining = 2;
        var tcs = new TaskCompletionSource<bool>();

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(async (string arg) =>
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        tcs.SetResult(true);
                    }

                    await tcs.Task;

                    return arg + arg;
                }, "Func"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func", arguments: new Dictionary<string, object?> { { "arg", "hello" } }),
                new FunctionCallContent("callId2", "Func", arguments: new Dictionary<string, object?> { { "arg", "world" } }),
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "hellohello"),
                new FunctionResultContent("callId2", result: "worldworld"),
            ]),
            new ChatMessage(ChatRole.Assistant, "done"),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s) { AllowConcurrentInvocation = true });

        await InvokeAndAssertAsync(options, plan, configurePipeline: configure);

        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
    }

    [Fact]
    public async Task ConcurrentInvocationOfParallelCallsDisabledByDefaultAsync()
    {
        int activeCount = 0;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(async (string arg) =>
                {
                    Interlocked.Increment(ref activeCount);
                    await Task.Delay(100);
                    Assert.Equal(1, activeCount);
                    Interlocked.Decrement(ref activeCount);
                    return arg + arg;
                }, "Func"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("callId1", "Func", arguments: new Dictionary<string, object?> { { "arg", "hello" } }),
                new FunctionCallContent("callId2", "Func", arguments: new Dictionary<string, object?> { { "arg", "world" } }),
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "hellohello"),
                new FunctionResultContent("callId2", result: "worldworld"),
            ]),
            new ChatMessage(ChatRole.Assistant, "done"),
        ];

        await InvokeAndAssertAsync(options, plan);

        await InvokeAndAssertStreamingAsync(options, plan);
    }

    [Fact]
    public async Task FunctionInvokerDelegateOverridesHandlingAsync()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
                AIFunctionFactory.Create((int i) => { }, "VoidReturn"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1 from delegate")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42 from delegate")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "VoidReturn", arguments: new Dictionary<string, object?> { { "i", 43 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Success: Function completed.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s)
            {
                FunctionInvoker = async (ctx, cancellationToken) =>
                {
                    Assert.NotNull(ctx);
                    var result = await ctx.Function.InvokeAsync(ctx.Arguments, cancellationToken);
                    return result is JsonElement e ?
                        JsonSerializer.SerializeToElement($"{e.GetString()} from delegate", AIJsonUtilities.DefaultOptions) :
                        result;
                }
            });

        await InvokeAndAssertAsync(options, plan, configurePipeline: configure);

        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionReturningFunctionResultContentWithMatchingCallId_UsesItDirectly(bool streaming)
    {
        FunctionResultContent? returnedFrc = null;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
            ]
        };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (msgs, opts, ct) =>
            {
                var toolMessage = msgs.FirstOrDefault(m => m.Role == ChatRole.Tool);
                if (toolMessage is null)
                {
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])));
                }
                else
                {
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
                }
            },
            GetStreamingResponseAsyncCallback = (msgs, opts, ct) =>
            {
                var toolMessage = msgs.FirstOrDefault(m => m.Role == ChatRole.Tool);
                if (toolMessage is null)
                {
                    return YieldAsync(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])).ToChatResponseUpdates());
                }
                else
                {
                    return YieldAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")).ToChatResponseUpdates());
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            FunctionInvoker = (ctx, cancellationToken) =>
            {
                returnedFrc = new FunctionResultContent(ctx.CallContent.CallId, "Custom result from function")
                {
                    RawRepresentation = "CustomRaw"
                };
                return new ValueTask<object?>(returnedFrc);
            }
        };

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "hello"),
        };

        ChatResponse response;
        if (streaming)
        {
            response = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
        }
        else
        {
            response = await client.GetResponseAsync(messages, options);
        }

        // Verify that the FunctionResultContent was used directly (same reference)
        var toolMessage = response.Messages.First(m => m.Role == ChatRole.Tool);
        var capturedFrc = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        Assert.Same(returnedFrc, capturedFrc);
        Assert.Equal("Custom result from function", capturedFrc.Result);
        Assert.Equal("CustomRaw", capturedFrc.RawRepresentation);
        Assert.Equal("callId1", capturedFrc.CallId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionReturningFunctionResultContentWithMismatchedCallId_WrapsIt(bool streaming)
    {
        FunctionResultContent? returnedFrc = null;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
            ]
        };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (msgs, opts, ct) =>
            {
                var toolMessage = msgs.FirstOrDefault(m => m.Role == ChatRole.Tool);
                if (toolMessage is null)
                {
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])));
                }
                else
                {
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
                }
            },
            GetStreamingResponseAsyncCallback = (msgs, opts, ct) =>
            {
                var toolMessage = msgs.FirstOrDefault(m => m.Role == ChatRole.Tool);
                if (toolMessage is null)
                {
                    return YieldAsync(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])).ToChatResponseUpdates());
                }
                else
                {
                    return YieldAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")).ToChatResponseUpdates());
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            FunctionInvoker = (ctx, cancellationToken) =>
            {
                // Return a FunctionResultContent with a different CallId
                returnedFrc = new FunctionResultContent("differentCallId", "Result from function");
                return new ValueTask<object?>(returnedFrc);
            }
        };

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "hello"),
        };

        ChatResponse response;
        if (streaming)
        {
            response = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
        }
        else
        {
            response = await client.GetResponseAsync(messages, options);
        }

        // Verify the result is wrapped - the outer FunctionResultContent has the correct CallId
        // and the inner one is reference-equal to what was returned
        var toolMessage = response.Messages.First(m => m.Role == ChatRole.Tool);
        var frc = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        Assert.Equal("callId1", frc.CallId);
        Assert.Same(returnedFrc, frc.Result);
        var innerFrc = (FunctionResultContent)frc.Result!;
        Assert.Equal("differentCallId", innerFrc.CallId);
        Assert.Equal("Result from function", innerFrc.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionReturningDerivedFunctionResultContent_PropagatesInstanceToInnerClient(bool streaming)
    {
        DerivedFunctionResultContent? returnedFrc = null;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
            ]
        };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (msgs, opts, ct) =>
            {
                var toolMessage = msgs.FirstOrDefault(m => m.Role == ChatRole.Tool);
                if (toolMessage is null)
                {
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])));
                }
                else
                {
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
                }
            },
            GetStreamingResponseAsyncCallback = (msgs, opts, ct) =>
            {
                var toolMessage = msgs.FirstOrDefault(m => m.Role == ChatRole.Tool);
                if (toolMessage is null)
                {
                    return YieldAsync(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])).ToChatResponseUpdates());
                }
                else
                {
                    return YieldAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")).ToChatResponseUpdates());
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            FunctionInvoker = (ctx, cancellationToken) =>
            {
                // Return a derived FunctionResultContent
                returnedFrc = new DerivedFunctionResultContent(ctx.CallContent.CallId, "Derived result")
                {
                    CustomProperty = "CustomValue"
                };
                return new ValueTask<object?>(returnedFrc);
            }
        };

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "hello"),
        };

        ChatResponse response;
        if (streaming)
        {
            response = await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
        }
        else
        {
            response = await client.GetResponseAsync(messages, options);
        }

        // Verify that the derived FunctionResultContent instance was propagated to the inner client
        // and is reference-equal to what was returned
        var toolMessage = response.Messages.First(m => m.Role == ChatRole.Tool);
        var capturedFrc = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        Assert.Same(returnedFrc, capturedFrc);
        Assert.IsType<DerivedFunctionResultContent>(capturedFrc);
        var derivedFrc = (DerivedFunctionResultContent)capturedFrc;
        Assert.Equal("callId1", derivedFrc.CallId);
        Assert.Equal("Derived result", derivedFrc.Result);
        Assert.Equal("CustomValue", derivedFrc.CustomProperty);
    }

    /// <summary>A derived FunctionResultContent for testing purposes.</summary>
    private sealed class DerivedFunctionResultContent : FunctionResultContent
    {
        public DerivedFunctionResultContent(string callId, object? result)
            : base(callId, result)
        {
        }

        public string? CustomProperty { get; set; }
    }

    [Fact]
    public async Task ContinuesWithSuccessfulCallsUntilMaximumIterations()
    {
        var maxIterations = 7;
        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline = pipeline => pipeline
            .UseFunctionInvocation(configure: functionInvokingChatClient =>
            {
                functionInvokingChatClient.MaximumIterationsPerRequest = maxIterations;
            });

        var actualCallCount = 0;
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => { actualCallCount++; }, "VoidReturn"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"callId0", "VoidReturn")]),
        ];

        // Note that this plan ends with a function call. Normally we would expect the system to try to resolve
        // the call, but it won't because of the maximum iterations limit.
        for (var i = 0; i < maxIterations; i++)
        {
            plan.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"callId{i}", result: "Success: Function completed.")]));
            plan.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"callId{(i + 1)}", "VoidReturn")]));
        }

        await InvokeAndAssertAsync(options, plan, configurePipeline: configurePipeline);
        Assert.Equal(maxIterations, actualCallCount);

        actualCallCount = 0;
        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configurePipeline);
        Assert.Equal(maxIterations, actualCallCount);
    }

    [Fact]
    public async Task LastIteration_RemovesFunctionDeclarationTools_NonStreaming()
    {
        List<ChatOptions?> capturedOptions = [];
        var maxIterations = 2;

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (contents, options, cancellationToken) =>
            {
                capturedOptions.Add(options?.Clone());

                var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"callId{capturedOptions.Count}", "Func1")]);
                return Task.FromResult(new ChatResponse(message));
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = maxIterations
        };

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result", "Func1")],
            ToolMode = ChatToolMode.Auto
        };

        await client.GetResponseAsync("hello", options);

        Assert.Equal(maxIterations + 1, capturedOptions.Count);

        for (int i = 0; i < maxIterations; i++)
        {
            Assert.NotNull(capturedOptions[i]?.Tools);
            Assert.Single(capturedOptions[i]!.Tools!);
        }

        var lastOptions = capturedOptions[maxIterations];
        Assert.NotNull(lastOptions);
        Assert.Null(lastOptions!.Tools);
        Assert.Null(lastOptions.ToolMode);
    }

    [Fact]
    public async Task LastIteration_RemovesFunctionDeclarationTools_Streaming()
    {
        List<ChatOptions?> capturedOptions = [];
        var maxIterations = 2;

        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (contents, options, cancellationToken) =>
            {
                capturedOptions.Add(options?.Clone());

                var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"callId{capturedOptions.Count}", "Func1")]);
                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = maxIterations
        };

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result", "Func1")],
            ToolMode = ChatToolMode.Auto
        };

        await client.GetStreamingResponseAsync("hello", options).ToChatResponseAsync();

        Assert.Equal(maxIterations + 1, capturedOptions.Count);

        for (int i = 0; i < maxIterations; i++)
        {
            Assert.NotNull(capturedOptions[i]?.Tools);
            Assert.Single(capturedOptions[i]!.Tools!);
        }

        var lastOptions = capturedOptions[maxIterations];
        Assert.NotNull(lastOptions);
        Assert.Null(lastOptions!.Tools);
        Assert.Null(lastOptions.ToolMode);
    }

    [Fact]
    public async Task LastIteration_PreservesNonFunctionDeclarationTools()
    {
        var hostedTool = new HostedWebSearchTool();
        List<ChatOptions?> capturedOptions = [];
        var maxIterations = 1;

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (contents, options, cancellationToken) =>
            {
                capturedOptions.Add(options?.Clone());

                if (capturedOptions.Count == 1)
                {
                    var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]);
                    return Task.FromResult(new ChatResponse(message));
                }
                else
                {
                    var message = new ChatMessage(ChatRole.Assistant, "Done");
                    return Task.FromResult(new ChatResponse(message));
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = maxIterations
        };

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result", "Func1"), hostedTool],
            ToolMode = ChatToolMode.Auto
        };

        await client.GetResponseAsync("hello", options);

        Assert.Equal(2, capturedOptions.Count);
        Assert.NotNull(capturedOptions[0]?.Tools);
        Assert.Equal(2, capturedOptions[0]!.Tools!.Count);

        Assert.NotNull(capturedOptions[1]?.Tools);
        Assert.Single(capturedOptions[1]!.Tools!);
        Assert.IsType<HostedWebSearchTool>(capturedOptions[1]!.Tools![0]);
        Assert.NotNull(capturedOptions[1]?.ToolMode);
    }

    [Fact]
    public async Task LastIteration_DoesNotModifyOriginalOptions()
    {
        List<ChatOptions?> capturedOptions = [];
        var maxIterations = 1;

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (contents, options, cancellationToken) =>
            {
                capturedOptions.Add(options);
                var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]);
                return Task.FromResult(new ChatResponse(message));
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = maxIterations
        };

        var originalTool = AIFunctionFactory.Create(() => "Result", "Func1");
        var originalOptions = new ChatOptions
        {
            Tools = [originalTool],
            ToolMode = ChatToolMode.Auto
        };

        await client.GetResponseAsync("hello", originalOptions);

        Assert.NotNull(originalOptions.Tools);
        Assert.Single(originalOptions.Tools);
        Assert.Same(originalTool, originalOptions.Tools[0]);
        Assert.NotNull(originalOptions.ToolMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuesWithFailingCallsUntilMaximumConsecutiveErrors(bool allowConcurrentInvocation)
    {
        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline = pipeline => pipeline
            .UseFunctionInvocation(configure: functionInvokingChatClient =>
            {
                functionInvokingChatClient.MaximumConsecutiveErrorsPerRequest = 2;
                functionInvokingChatClient.AllowConcurrentInvocation = allowConcurrentInvocation;
            });

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create((bool shouldThrow, int callIndex) =>
                {
                    if (shouldThrow)
                    {
                        throw new InvalidTimeZoneException($"Exception from call {callIndex}");
                    }
                }, "Func"),
            ]
        };

        var callIndex = 0;
        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),

            // A single failure isn't enough to stop the cycle
            ..CreateFunctionCallIterationPlan(ref callIndex, true, false),

            // Now NumConsecutiveErrors = 1
            // We can reset the number of consecutive errors by having a successful iteration
            ..CreateFunctionCallIterationPlan(ref callIndex, false, false, false),

            // Now NumConsecutiveErrors = 0
            // Any failure within an iteration causes the whole iteration to be treated as failed
            ..CreateFunctionCallIterationPlan(ref callIndex, false, true, false),

            // Now NumConsecutiveErrors = 1
            // Even if several calls in the same iteration fail, that only counts as a single iteration having failed, so won't exceed the limit yet
            ..CreateFunctionCallIterationPlan(ref callIndex, true, true, true),

            // Now NumConsecutiveErrors = 2
            // Any more failures will now exceed the limit
            ..CreateFunctionCallIterationPlan(ref callIndex, true, true),
        ];

        if (allowConcurrentInvocation)
        {
            // With concurrent invocation, we always make all the calls in the iteration
            // and combine their exceptions into an AggregateException
            var ex = await Assert.ThrowsAsync<AggregateException>(() =>
                InvokeAndAssertAsync(options, plan, configurePipeline: configurePipeline));
            Assert.Equal(2, ex.InnerExceptions.Count);
            Assert.Equal("Exception from call 11", ex.InnerExceptions[0].Message);
            Assert.Equal("Exception from call 12", ex.InnerExceptions[1].Message);

            ex = await Assert.ThrowsAsync<AggregateException>(() =>
                InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configurePipeline));
            Assert.Equal(2, ex.InnerExceptions.Count);
            Assert.Equal("Exception from call 11", ex.InnerExceptions[0].Message);
            Assert.Equal("Exception from call 12", ex.InnerExceptions[1].Message);
        }
        else
        {
            // With serial invocation, we allow the threshold-crossing exception to propagate
            // directly and terminate the iteration
            var ex = await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
                InvokeAndAssertAsync(options, plan, configurePipeline: configurePipeline));
            Assert.Equal("Exception from call 11", ex.Message);

            ex = await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
                InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configurePipeline));
            Assert.Equal("Exception from call 11", ex.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanFailOnFirstException(bool allowConcurrentInvocation)
    {
        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline = pipeline => pipeline
            .UseFunctionInvocation(configure: functionInvokingChatClient =>
            {
                functionInvokingChatClient.MaximumConsecutiveErrorsPerRequest = 0;
                functionInvokingChatClient.AllowConcurrentInvocation = allowConcurrentInvocation;
            });

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() =>
                {
                    throw new InvalidTimeZoneException($"It failed");
                }, "Func"),
            ]
        };

        var callIndex = 0;
        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            ..CreateFunctionCallIterationPlan(ref callIndex, true),
        ];

        // Regardless of AllowConcurrentInvocation, if there's only a single exception,
        // we don't wrap it in an AggregateException
        var ex = await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
            InvokeAndAssertAsync(options, plan, configurePipeline: configurePipeline));
        Assert.Equal("It failed", ex.Message);

        ex = await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
            InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configurePipeline));
        Assert.Equal("It failed", ex.Message);
    }

    private static IEnumerable<ChatMessage> CreateFunctionCallIterationPlan(ref int callIndex, params bool[] shouldThrow)
    {
        var assistantMessage = new ChatMessage(ChatRole.Assistant, []);
        var toolMessage = new ChatMessage(ChatRole.Tool, []);

        foreach (var callShouldThrow in shouldThrow)
        {
            var thisCallIndex = callIndex++;
            var callId = $"callId{thisCallIndex}";
            assistantMessage.Contents.Add(new FunctionCallContent(callId, "Func",
                arguments: new Dictionary<string, object?> { { "shouldThrow", callShouldThrow }, { "callIndex", thisCallIndex } }));
            toolMessage.Contents.Add(new FunctionResultContent(callId, result: callShouldThrow ? "Error: Function failed." : "Success"));
        }

        return [assistantMessage, toolMessage];
    }

    [Fact]
    public async Task KeepsFunctionCallingContent()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Result 1", "Func1"),
                AIFunctionFactory.Create((int i) => $"Result 2: {i}", "Func2"),
                AIFunctionFactory.Create((int i) => { }, "VoidReturn"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new TextContent("extra"), new FunctionCallContent("callId1", "Func1"), new TextContent("stuff")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func2", arguments: new Dictionary<string, object?> { { "i", 42 } })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 2: 42")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "VoidReturn", arguments: new Dictionary<string, object?> { { "i", 43 } }), new TextContent("more")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Success: Function completed.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

#pragma warning disable SA1005, S125
        Validate(await InvokeAndAssertAsync(options, plan));

        Validate(await InvokeAndAssertStreamingAsync(options, plan));

        static void Validate(List<ChatMessage> finalChat)
        {
            IEnumerable<AIContent> content = finalChat.SelectMany(m => m.Contents);
            Assert.Contains(content, c => c is FunctionCallContent or FunctionResultContent);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionDetailsOnlyReportedWhenRequestedAsync(bool detailedErrors)
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(string () => throw new InvalidOperationException("Oh no!"), "Func1"),
            ]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: detailedErrors ? "Error: Function failed. Exception: Oh no!" : "Error: Function failed.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s) { IncludeDetailedErrors = detailedErrors });

        await InvokeAndAssertAsync(options, plan, configurePipeline: configure);

        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    public async Task FunctionInvocationsLogged(LogLevel level)
    {
        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["arg1"] = "value1" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b =>
            b.Use((c, services) => new FunctionInvokingChatClient(c, services.GetRequiredService<ILoggerFactory>()));

        await InvokeAsync(services => InvokeAndAssertAsync(options, plan, configurePipeline: configure, services: services));

        await InvokeAsync(services => InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure, services: services));

        async Task InvokeAsync(Func<IServiceProvider, Task> work)
        {
            var collector = new FakeLogCollector();

            ServiceCollection c = new();
            c.AddLogging(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(level));

            await work(c.BuildServiceProvider());

            var logs = collector.GetSnapshot();
            if (level is LogLevel.Trace)
            {
                Assert.Collection(logs,
                    entry => Assert.True(entry.Message.Contains("Invoking Func1({") && entry.Message.Contains("\"arg1\": \"value1\"")),
                    entry => Assert.True(entry.Message.Contains("Func1 invocation completed. Duration:") && entry.Message.Contains("Result: \"Result 1\"")));
            }
            else if (level is LogLevel.Debug)
            {
                Assert.Collection(logs,
                    entry => Assert.True(entry.Message.Contains("Invoking Func1") && !entry.Message.Contains("arg1")),
                    entry => Assert.True(entry.Message.Contains("Func1 invocation completed. Duration:") && !entry.Message.Contains("Result")));
            }
            else
            {
                Assert.Empty(logs);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FunctionInvocationTrackedWithActivity(bool enableTelemetry, bool enableSensitiveData)
    {
        string sourceName = Guid.NewGuid().ToString();

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["arg1"] = "value1" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(c =>
            new FunctionInvokingChatClient(new OpenTelemetryChatClient(c, sourceName: sourceName) { EnableSensitiveData = enableSensitiveData }));

        await InvokeAsync(() => InvokeAndAssertAsync(options, plan, configurePipeline: configure));

        await InvokeAsync(() => InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure));

        async Task InvokeAsync(Func<Task> work)
        {
            var activities = new List<Activity>();
            using TracerProvider? tracerProvider = enableTelemetry ?
                OpenTelemetry.Sdk.CreateTracerProviderBuilder()
                .AddSource(sourceName)
                .AddInMemoryExporter(activities)
                .Build() :
                null;

            await work();

            if (enableTelemetry)
            {
                Assert.Collection(activities,
                    activity => Assert.Equal("chat", activity.DisplayName),
                    activity => Assert.Equal("execute_tool Func1", activity.DisplayName),
                    activity => Assert.Equal("chat", activity.DisplayName),
                    activity => Assert.Equal("orchestrate_tools", activity.DisplayName));

                var executeTool = activities[1];
                if (enableSensitiveData)
                {
                    var args = Assert.Single(executeTool.Tags, t => t.Key == "gen_ai.tool.call.arguments");
                    Assert.Equal(
                        JsonSerializer.Serialize(new Dictionary<string, object?> { ["arg1"] = "value1" }, AIJsonUtilities.DefaultOptions),
                        args.Value);

                    var result = Assert.Single(executeTool.Tags, t => t.Key == "gen_ai.tool.call.result");
                    Assert.Equal("Result 1", JsonSerializer.Deserialize<string>(result.Value!, AIJsonUtilities.DefaultOptions));
                }
                else
                {
                    Assert.DoesNotContain(executeTool.Tags, t => t.Key == "gen_ai.tool.call.arguments");
                    Assert.DoesNotContain(executeTool.Tags, t => t.Key == "gen_ai.tool.call.result");
                }

                for (int i = 0; i < activities.Count - 1; i++)
                {
                    // Activities are exported in the order of completion, so all except the last are children of the last (i.e., outer)
                    Assert.Same(activities[activities.Count - 1], activities[i].Parent);
                }
            }
            else
            {
                Assert.Empty(activities);
            }
        }
    }

    [Fact]
    public async Task SupportsConsecutiveStreamingUpdatesWithFunctionCalls()
    {
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string text) => $"Result for {text}", "Func1")]
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
        };

        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (chatContents, chatOptions, cancellationToken) =>
            {
                // If the conversation is just starting, issue two consecutive updates with function calls
                // Otherwise just end the conversation.
                List<ChatResponseUpdate> updates;
                string messageId = Guid.NewGuid().ToString("N");
                if (chatContents.Last().Text == "Hello")
                {
                    updates =
                    [
                        new() { Contents = [new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["text"] = "Input 1" })] },
                        new() { Contents = [new FunctionCallContent("callId2", "Func1", new Dictionary<string, object?> { ["text"] = "Input 2" })] }
                    ];
                }
                else
                {
                    updates = [new() { Contents = [new TextContent("OK bye")] }];
                }

                foreach (var update in updates)
                {
                    update.MessageId = messageId;
                }

                return YieldAsync(updates);
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        var response = await client.GetStreamingResponseAsync(messages, options, CancellationToken.None).ToChatResponseAsync();

        // The returned message should include the FCCs and FRCs.
        Assert.Collection(response.Messages,
            m => Assert.Collection(m.Contents,
                c => Assert.Equal("Input 1", Assert.IsType<FunctionCallContent>(c).Arguments!["text"]),
                c => Assert.Equal("Input 2", Assert.IsType<FunctionCallContent>(c).Arguments!["text"])),
            m => Assert.Collection(m.Contents,
                c => Assert.Equal("Result for Input 1", Assert.IsType<FunctionResultContent>(c).Result?.ToString()),
                c => Assert.Equal("Result for Input 2", Assert.IsType<FunctionResultContent>(c).Result?.ToString())),
            m => Assert.Equal("OK bye", Assert.IsType<TextContent>(Assert.Single(m.Contents)).Text));
    }

    [Fact]
    public async Task AllResponseMessagesReturned()
    {
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "doesn't matter", "Func1")]
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
        };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (chatContents, chatOptions, cancellationToken) =>
            {
                await Task.Yield();

                ChatMessage message = chatContents.Count() is 1 or 3 ?
                    new(ChatRole.Assistant, [new FunctionCallContent($"callId{chatContents.Count()}", "Func1")]) :
                    new(ChatRole.Assistant, "The answer is 42.");

                return new(message);
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        ChatResponse response = await client.GetResponseAsync(messages, options);

        Assert.Equal(5, response.Messages.Count);
        Assert.Equal("The answer is 42.", response.Text);
        Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[0].Contents));
        Assert.IsType<FunctionResultContent>(Assert.Single(response.Messages[1].Contents));
        Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[2].Contents));
        Assert.IsType<FunctionResultContent>(Assert.Single(response.Messages[3].Contents));
        Assert.IsType<TextContent>(Assert.Single(response.Messages[4].Contents));
    }

    [Fact]
    public async Task CanAccesssFunctionInvocationContextFromFunctionCall()
    {
        var invocationContexts = new List<FunctionInvocationContext>();
        var function = AIFunctionFactory.Create(async (int i) =>
        {
            // The context should propogate across async calls
            await Task.Yield();

            var context = FunctionInvokingChatClient.CurrentContext!;
            invocationContexts.Add(context);

            if (i == 42)
            {
                context.Terminate = true;
            }

            return $"Result {i}";
        }, "Func1");

        var options = new ChatOptions
        {
            Tools = [function],
        };

        // The invocation loop should terminate after the second function call
        List<ChatMessage> planBeforeTermination =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["i"] = 41 })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 41")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId2", "Func1", new Dictionary<string, object?> { ["i"] = 42 })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId2", result: "Result 42")]),
        ];

        // The full plan should never be fulfilled
        List<ChatMessage> plan =
        [
            .. planBeforeTermination,
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId3", "Func1", new Dictionary<string, object?> { ["i"] = 43 })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId3", result: "Result 43")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAsync(() => InvokeAndAssertAsync(options, plan, planBeforeTermination));

        await InvokeAsync(() => InvokeAndAssertStreamingAsync(options, plan, planBeforeTermination));

        // The current context should be null outside the async call stack for the function invocation
        Assert.Null(FunctionInvokingChatClient.CurrentContext);

        async Task InvokeAsync(Func<Task<List<ChatMessage>>> work)
        {
            invocationContexts.Clear();

            var messages = await work();

            Assert.Collection(invocationContexts,
                c => AssertInvocationContext(c, iteration: 0, terminate: false),
                c => AssertInvocationContext(c, iteration: 1, terminate: true));

            void AssertInvocationContext(FunctionInvocationContext context, int iteration, bool terminate)
            {
                Assert.NotNull(context);
                Assert.Equal(messages.Count, context.Messages.Count);
                Assert.Equal(string.Concat(messages), string.Concat(context.Messages));
                Assert.Same(function, context.Function);
                Assert.Equal("Func1", context.CallContent.Name);
                Assert.Equal(0, context.FunctionCallIndex);
                Assert.Equal(1, context.FunctionCount);
                Assert.Equal(iteration, context.Iteration);
                Assert.Equal(terminate, context.Terminate);
            }
        }
    }

    [Fact]
    public async Task HaltFunctionCallingAfterTermination()
    {
        var function = AIFunctionFactory.Create((string? result = null) =>
        {
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            FunctionInvokingChatClient.CurrentContext!.Terminate = true;
            return (object?)null;
        }, "Search");

        using var innerChatClient = new TestChatClient
        {
            GetResponseAsyncCallback = (chatContents, chatOptions, cancellationToken) =>
            {
                // We can have a mixture of calls that are not terminated and terminated
                var existingSearchResult = chatContents.SingleOrDefault(m => m.Role == ChatRole.Tool);
                AIContent[] resultContents = existingSearchResult is not null && existingSearchResult.Contents.OfType<FunctionResultContent>().ToList() is { } frcs
                    ? [new TextContent($"The search results were '{string.Join(", ", frcs.Select(frc => frc.Result))}'")]
                    : [
                        new FunctionCallContent("callId1", "Search"),
                        new FunctionCallContent("callId2", "Search", new Dictionary<string, object?> { { "result", "birds" } }),
                        new FunctionCallContent("callId3", "Search"),
                      ];

                var message = new ChatMessage(ChatRole.Assistant, resultContents);
                return Task.FromResult(new ChatResponse(message));
            }
        };
        using var chatClient = new FunctionInvokingChatClient(innerChatClient);

        // The function should terminate the invocation loop without calling the inner client for a final answer
        // But it still makes all the function calls within the same iteration
        List<ChatMessage> messages = [new(ChatRole.User, "hello")];
        var chatOptions = new ChatOptions { Tools = [function] };
        var result = await chatClient.GetResponseAsync(messages, chatOptions);
        messages.AddMessages(result);

        // Application code can then set the results
        var lastMessage = messages.Last();
        Assert.Equal(ChatRole.Tool, lastMessage.Role);
        var frcs = lastMessage.Contents.OfType<FunctionResultContent>().ToList();
        Assert.Single(frcs);
        frcs[0].Result = "dogs";

        // We can re-enter the function calling mechanism to get a final answer
        result = await chatClient.GetResponseAsync(messages, chatOptions);
        Assert.Equal("The search results were 'dogs'", result.Text);
    }

    [Fact]
    public async Task TerminationIsHonoredWhenFunctionThrows()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(string () =>
                {
                    FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                    throw new InvalidOperationException("Oh no!");
                }, "Func1"),
            ]
        };

        List<ChatMessage> planBeforeTermination =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Error: Function failed.")]),
        ];

        // The inner client must not be asked for a final answer after the function requested termination
        List<ChatMessage> plan =
        [
            .. planBeforeTermination,
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, plan, planBeforeTermination);

        await InvokeAndAssertStreamingAsync(options, plan, planBeforeTermination);
    }

    [Fact]
    public async Task TerminationIsHonoredWhenFunctionThrows_SerialStopsRemainingCalls()
    {
        int func2Invocations = 0;
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(string () =>
                {
                    FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                    throw new InvalidOperationException("Oh no!");
                }, "Func1"),
                AIFunctionFactory.Create(() =>
                {
                    Interlocked.Increment(ref func2Invocations);
                    return "Result 2";
                }, "Func2"),
            ]
        };

        // Termination in serial mode stops processing of subsequent calls in the same iteration,
        // matching the behavior when a function requests termination without throwing.
        List<ChatMessage> planBeforeTermination =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Error: Function failed.")]),
        ];

        List<ChatMessage> plan =
        [
            .. planBeforeTermination,
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        await InvokeAndAssertAsync(options, plan, planBeforeTermination);

        await InvokeAndAssertStreamingAsync(options, plan, planBeforeTermination);

        Assert.Equal(0, func2Invocations);
    }

    [Fact]
    public async Task TerminationIsHonoredWhenFunctionThrows_Concurrent()
    {
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(string () =>
                {
                    FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                    throw new InvalidOperationException("Oh no!");
                }, "Func1"),
                AIFunctionFactory.Create(() => "Result 2", "Func2"),
            ]
        };

        // With concurrent invocation, all calls in the iteration run, and then the loop terminates.
        List<ChatMessage> planBeforeTermination =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2")]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId1", result: "Error: Function failed."),
                new FunctionResultContent("callId2", result: "Result 2"),
            ]),
        ];

        List<ChatMessage> plan =
        [
            .. planBeforeTermination,
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s) { AllowConcurrentInvocation = true });

        await InvokeAndAssertAsync(options, plan, planBeforeTermination, configure);

        await InvokeAndAssertStreamingAsync(options, plan, planBeforeTermination, configure);
    }

    [Fact]
    public async Task TerminationWhenFunctionThrows_PreservesExceptionOnResult()
    {
        var exception = new InvalidOperationException("Oh no!");
        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(string () =>
                {
                    FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                    throw exception;
                }, "Func1"),
            ]
        };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (contents, _, _) =>
            {
                Assert.DoesNotContain(contents, m => m.Role == ChatRole.Tool);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")])));
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);
        var response = await client.GetResponseAsync("hello", options);

        Assert.Equal(ChatRole.Tool, response.Messages.Last().Role);
        var frc = Assert.IsType<FunctionResultContent>(Assert.Single(response.Messages.Last().Contents));
        Assert.Same(exception, frc.Exception);
        Assert.Null(FunctionInvokingChatClient.CurrentContext);
    }

    [Fact]
    public async Task PropagatesResponseConversationIdToOptions()
    {
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")],
        };

        int iteration = 0;

        Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, ChatResponse> callback =
            (chatContents, chatOptions, cancellationToken) =>
            {
                iteration++;

                if (iteration == 1)
                {
                    Assert.Null(chatOptions?.ConversationId);
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId-abc", "Func1")]))
                    {
                        ConversationId = "12345",
                    };
                }
                else if (iteration == 2)
                {
                    Assert.Equal("12345", chatOptions?.ConversationId);
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done!"));
                }
                else
                {
                    throw new InvalidOperationException("Unexpected iteration");
                }
            };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (chatContents, chatOptions, cancellationToken) =>
                Task.FromResult(callback(chatContents, chatOptions, cancellationToken)),
            GetStreamingResponseAsyncCallback = (chatContents, chatOptions, cancellationToken) =>
                YieldAsync(callback(chatContents, chatOptions, cancellationToken).ToChatResponseUpdates()),
        };

        using IChatClient service = innerClient.AsBuilder().UseFunctionInvocation().Build();

        iteration = 0;
        Assert.Equal("done!", (await service.GetResponseAsync("hey", options)).ToString());
        iteration = 0;
        Assert.Equal("done!", (await service.GetStreamingResponseAsync("hey", options).ToChatResponseAsync()).ToString());
    }

    [Fact]
    public async Task FunctionInvocations_PassesServices()
    {
        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["arg1"] = "value1" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ServiceCollection c = new();
        IServiceProvider expected = c.BuildServiceProvider();

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((IServiceProvider actual) =>
            {
                Assert.Same(expected, actual);
                return "Result 1";
            }, "Func1")]
        };

        await InvokeAndAssertAsync(options, plan, services: expected);
    }

    [Fact]
    public async Task FunctionInvocations_InvokedOnOriginalSynchronizationContext()
    {
        SynchronizationContext ctx = new CustomSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ctx);

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["arg"] = "value1" }),
                new FunctionCallContent("callId2", "Func1", new Dictionary<string, object?> { ["arg"] = "value2" }),
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("callId2", result: "value1"),
                new FunctionResultContent("callId2", result: "value2")
            ]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(async (string arg, CancellationToken cancellationToken) =>
            {
                await Task.Delay(1, cancellationToken);
                Assert.Same(ctx, SynchronizationContext.Current);
                return arg;
            }, "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline = builder => builder
            .Use(async (messages, options, next, cancellationToken) =>
            {
                await Task.Delay(1, cancellationToken);
                await next(messages, options, cancellationToken);
            })
            .UseOpenTelemetry()
            .UseFunctionInvocation(configure: c => { c.AllowConcurrentInvocation = true; c.IncludeDetailedErrors = true; });

        await InvokeAndAssertAsync(options, plan, configurePipeline: configurePipeline);
        await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configurePipeline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminateOnUnknownCalls_ControlsBehaviorForUnknownFunctions(bool terminateOnUnknown)
    {
        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create((int i) => $"Known: {i}", "KnownFunc")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s) { TerminateOnUnknownCalls = terminateOnUnknown });

        if (!terminateOnUnknown)
        {
            List<ChatMessage> planForContinue =
            [
                new(ChatRole.User, "hello"),
                new(ChatRole.Assistant, [
                    new FunctionCallContent("callId1", "UnknownFunc", new Dictionary<string, object?> { ["i"] = 1 }),
                    new FunctionCallContent("callId2", "KnownFunc", new Dictionary<string, object?> { ["i"] = 2 })
                ]),
                new(ChatRole.Tool, [
                    new FunctionResultContent("callId1", result: "Error: Requested function \"UnknownFunc\" not found."),
                    new FunctionResultContent("callId2", result: "Known: 2")
                ]),
                new(ChatRole.Assistant, "done"),
            ];

            await InvokeAndAssertAsync(options, planForContinue, configurePipeline: configure);
            await InvokeAndAssertStreamingAsync(options, planForContinue, configurePipeline: configure);
        }
        else
        {
            List<ChatMessage> fullPlanWithUnknown =
            [
                new(ChatRole.User, "hello"),
                new(ChatRole.Assistant, [
                    new FunctionCallContent("callId1", "UnknownFunc", new Dictionary<string, object?> { ["i"] = 1 }),
                    new FunctionCallContent("callId2", "KnownFunc", new Dictionary<string, object?> { ["i"] = 2 })
                ]),
                new(ChatRole.Tool, [
                    new FunctionResultContent("callId1", result: "Error: Requested function \"UnknownFunc\" not found."),
                    new FunctionResultContent("callId2", result: "Known: 2")
                ]),
                new(ChatRole.Assistant, "done"),
            ];

            var expected = fullPlanWithUnknown.Take(2).ToList();
            await InvokeAndAssertAsync(options, fullPlanWithUnknown, expected, configure);
            await InvokeAndAssertStreamingAsync(options, fullPlanWithUnknown, expected, configure);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestsWithOnlyFunctionDeclarations_TerminatesRegardlessOfTerminateOnUnknownCalls(bool terminateOnUnknown)
    {
        var declarationOnly = AIFunctionFactory.Create(() => "unused", "DefOnly").AsDeclarationOnly();

        ChatOptions options = new() { Tools = [declarationOnly] };

        List<ChatMessage> fullPlan =
        [
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, [new FunctionCallContent("callId1", "DefOnly")]),
            new(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Should not be produced")]),
            new(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> expected = fullPlan.Take(2).ToList();

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(
            s => new FunctionInvokingChatClient(s) { TerminateOnUnknownCalls = terminateOnUnknown });

        await InvokeAndAssertAsync(options, fullPlan, expected, configure);
        await InvokeAndAssertStreamingAsync(options, fullPlan, expected, configure);
    }

    [Fact]
    public async Task MixedKnownFunctionAndDeclaration_TerminatesWithoutInvokingKnown()
    {
        int invoked = 0;
        var known = AIFunctionFactory.Create(() => { invoked++; return "OK"; }, "Known");
        var defOnly = AIFunctionFactory.Create(() => "unused", "DefOnly").AsDeclarationOnly();

        var options = new ChatOptions
        {
            Tools = [known, defOnly]
        };

        List<ChatMessage> fullPlan =
        [
            new(ChatRole.User, "hi"),
            new(ChatRole.Assistant, [
                new FunctionCallContent("callId1", "Known"),
                new FunctionCallContent("callId2", "DefOnly")
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("callId1", result: "OK"), new FunctionResultContent("callId2", result: "nope")]),
            new(ChatRole.Assistant, "done"),
        ];

        List<ChatMessage> expected = fullPlan.Take(2).ToList();

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(s => new FunctionInvokingChatClient(s) { TerminateOnUnknownCalls = false });
        await InvokeAndAssertAsync(options, fullPlan, expected, configure);
        Assert.Equal(0, invoked);

        invoked = 0;
        configure = b => b.Use(s => new FunctionInvokingChatClient(s) { TerminateOnUnknownCalls = true });
        await InvokeAndAssertStreamingAsync(options, fullPlan, expected, configure);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task ClonesChatOptionsAndResetContinuationTokenForBackgroundResponsesAsync()
    {
        ChatOptions? actualChatOptions = null;

        using var innerChatClient = new TestChatClient
        {
            GetResponseAsyncCallback = (chatContents, chatOptions, cancellationToken) =>
            {
                actualChatOptions = chatOptions;

                List<ChatMessage> messages = [];

                // Simulate the model returning a function call for the first call only
                if (!chatContents.Any(m => m.Contents.OfType<FunctionCallContent>().Any()))
                {
                    messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]));
                }

                return Task.FromResult(new ChatResponse { Messages = messages });
            }
        };

        using var chatClient = new FunctionInvokingChatClient(innerChatClient);

        var originalChatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => { }, "Func1")],
            ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3, 4 }),
        };

        await chatClient.GetResponseAsync("hi", originalChatOptions);

        // The original options should be cloned and have a null ContinuationToken
        Assert.NotSame(originalChatOptions, actualChatOptions);
        Assert.Null(actualChatOptions!.ContinuationToken);
    }

    [Theory]
    [InlineData("invoke_agent")]
    [InlineData("invoke_agent my_agent")]
    [InlineData("invoke_agent ")]
    [InlineData("invoke_workflow")]
    [InlineData("invoke_workflow my_workflow")]
    [InlineData("invoke_workflow ")]
    public async Task DoesNotCreateOrchestrateToolsSpanWhenInvokeAgentIsParent(string displayName)
    {
        string agentSourceName = Guid.NewGuid().ToString();
        string clientSourceName = Guid.NewGuid().ToString();

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(c =>
            new FunctionInvokingChatClient(new OpenTelemetryChatClient(c, sourceName: clientSourceName)));

        var activities = new List<Activity>();

        using TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(agentSourceName)
            .AddSource(clientSourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using (var agentSource = new ActivitySource(agentSourceName))
        using (var invokeAgentActivity = agentSource.StartActivity(displayName))
        {
            Assert.NotNull(invokeAgentActivity);
            await InvokeAndAssertAsync(options, plan, configurePipeline: configure);
        }

        Assert.DoesNotContain(activities, a => a.DisplayName == "orchestrate_tools");
        Assert.Contains(activities, a => a.DisplayName == "chat");
        Assert.Contains(activities, a => a.DisplayName == "execute_tool Func1");

        var invokeAgent = Assert.Single(activities, a => a.DisplayName == displayName);
        var childActivities = activities.Where(a => a != invokeAgent).ToList();
        Assert.All(childActivities, activity => Assert.Same(invokeAgent, activity.Parent));
    }

    [Theory]
    [InlineData("invoke_agent MyAgent(agent-123)")]
    [InlineData("invoke_workflow MyWorkflow(workflow-123)")]
    public async Task StreamingPreservesTraceContextWhenInvokeAgentWithNameIsParent(string displayName)
    {
        string agentSourceName = Guid.NewGuid().ToString();
        string clientSourceName = Guid.NewGuid().ToString();
        var activities = new List<Activity>();

        using TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(agentSourceName)
            .AddSource(clientSourceName)
            .AddInMemoryExporter(activities)
            .Build();

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (messages, options, ct) =>
            {
                callCount++;
                ChatMessage message = callCount == 1
                    ? new(ChatRole.Assistant, [new FunctionCallContent("call1", "Func1")])
                    : new(ChatRole.Assistant, "Done");
                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        var client = innerClient.AsBuilder()
            .Use(c => new FunctionInvokingChatClient(
                new OpenTelemetryChatClient(c, sourceName: clientSourceName)))
            .Build();

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        using var agentSource = new ActivitySource(agentSourceName);
        using var invokeAgentActivity = agentSource.StartActivity(displayName);
        Assert.NotNull(invokeAgentActivity);

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")], options))
        {
            // consume all updates
        }

        Assert.Equal(2, callCount);

        var chatActivities = activities.Where(a => a.DisplayName.StartsWith("chat", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, chatActivities.Count);

        // All child activities must share the same trace as the parent activity
        var nonAgentActivities = activities.Where(a => a != invokeAgentActivity).ToList();
        Assert.All(nonAgentActivities, a =>
            Assert.Equal(invokeAgentActivity.TraceId, a.TraceId));
    }

    [Theory]
    [InlineData("invoke_agen")]
    [InlineData("invoke_agent_extra")]
    [InlineData("invoke_agentx")]
    [InlineData("invoke_workflo")]
    [InlineData("invoke_workflow_extra")]
    [InlineData("invoke_workflowx")]
    public async Task CreatesOrchestrateToolsSpanWhenParentIsNotInvokeAgent(string displayName)
    {
        string agentSourceName = Guid.NewGuid().ToString();
        string clientSourceName = Guid.NewGuid().ToString();

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(c =>
            new FunctionInvokingChatClient(new OpenTelemetryChatClient(c, sourceName: clientSourceName)));

        var activities = new List<Activity>();

        using TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(agentSourceName)
            .AddSource(clientSourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using (var agentSource = new ActivitySource(agentSourceName))
        using (var invokeAgentActivity = agentSource.StartActivity(displayName))
        {
            Assert.NotNull(invokeAgentActivity);
            await InvokeAndAssertAsync(options, plan, configurePipeline: configure);
        }

        Assert.Contains(activities, a => a.DisplayName == "orchestrate_tools");
    }

    [Theory]
    [InlineData("invoke_agent")]
    [InlineData("invoke_workflow")]
    public async Task UsesAgentActivitySourceWhenInvokeAgentIsParent(string displayName)
    {
        string agentSourceName = Guid.NewGuid().ToString();
        string clientSourceName = Guid.NewGuid().ToString();

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(c =>
            new FunctionInvokingChatClient(new OpenTelemetryChatClient(c, sourceName: clientSourceName)));

        var activities = new List<Activity>();

        using TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(agentSourceName)
            .AddSource(clientSourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using (var agentSource = new ActivitySource(agentSourceName))
        using (var invokeAgentActivity = agentSource.StartActivity(displayName))
        {
            Assert.NotNull(invokeAgentActivity);
            await InvokeAndAssertAsync(options, plan, configurePipeline: configure);
        }

        var executeToolActivities = activities.Where(a => a.DisplayName == "execute_tool Func1").ToList();
        Assert.NotEmpty(executeToolActivities);
        Assert.All(executeToolActivities, executeTool => Assert.Equal(agentSourceName, executeTool.Source.Name));
    }

    public static IEnumerable<object[]> SensitiveDataPropagatesFromAgentActivityWhenInvokeAgentIsParent_MemberData() =>
        from operationName in new[] { "invoke_agent", "invoke_workflow" }
        from invokeAgentSensitiveData in new bool?[] { null, false, true }
        from innerOpenTelemetryChatClient in new bool?[] { null, false, true }
        select new object?[] { operationName, invokeAgentSensitiveData, innerOpenTelemetryChatClient };

    [Theory]
    [MemberData(nameof(SensitiveDataPropagatesFromAgentActivityWhenInvokeAgentIsParent_MemberData))]
    public async Task SensitiveDataPropagatesFromAgentActivityWhenInvokeAgentIsParent(
        string operationName, bool? invokeAgentSensitiveData, bool? innerOpenTelemetryChatClient)
    {
        string agentSourceName = Guid.NewGuid().ToString();
        string clientSourceName = Guid.NewGuid().ToString();

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1", new Dictionary<string, object?> { ["arg1"] = "secret" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        var activities = new List<Activity>();

        using TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(agentSourceName)
            .AddSource(clientSourceName)
            .AddInMemoryExporter(activities)
            .Build();

        using (var agentSource = new ActivitySource(agentSourceName))
        using (var invokeAgentActivity = agentSource.StartActivity(operationName))
        {
            if (invokeAgentSensitiveData is not null)
            {
                invokeAgentActivity?.SetCustomProperty("__EnableSensitiveData__", invokeAgentSensitiveData is true ? "true" : "false");
            }

            await InvokeAndAssertAsync(options, plan, configurePipeline: b =>
            {
                b.UseFunctionInvocation();

                if (innerOpenTelemetryChatClient is not null)
                {
                    b.UseOpenTelemetry(sourceName: clientSourceName, configure: c =>
                    {
                        c.EnableSensitiveData = innerOpenTelemetryChatClient.Value;
                    });
                }

                return b;
            });
        }

        var executeToolActivity = Assert.Single(activities, a => a.DisplayName == "execute_tool Func1");

        var hasArguments = executeToolActivity.Tags.Any(t => t.Key == "gen_ai.tool.call.arguments");
        var hasResult = executeToolActivity.Tags.Any(t => t.Key == "gen_ai.tool.call.result");

        if (invokeAgentSensitiveData is true)
        {
            Assert.True(hasArguments, "Expected arguments to be logged when agent EnableSensitiveData is true");
            Assert.True(hasResult, "Expected result to be logged when agent EnableSensitiveData is true");

            var argsTag = Assert.Single(executeToolActivity.Tags, t => t.Key == "gen_ai.tool.call.arguments");
            Assert.Contains("arg1", argsTag.Value);
        }
        else
        {
            Assert.False(hasArguments, "Expected arguments NOT to be logged when agent EnableSensitiveData is false");
            Assert.False(hasResult, "Expected result NOT to be logged when agent EnableSensitiveData is false");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatesOrchestrateToolsSpanWhenNoInvokeAgentParent(bool streaming)
    {
        string clientSourceName = Guid.NewGuid().ToString();

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        ChatOptions options = new()
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        Func<ChatClientBuilder, ChatClientBuilder> configure = b => b.Use(c =>
            new FunctionInvokingChatClient(new OpenTelemetryChatClient(c, sourceName: clientSourceName)));

        var activities = new List<Activity>();
        using TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(clientSourceName)
            .AddInMemoryExporter(activities)
            .Build();

        if (streaming)
        {
            await InvokeAndAssertStreamingAsync(options, plan, configurePipeline: configure);
        }
        else
        {
            await InvokeAndAssertAsync(options, plan, configurePipeline: configure);
        }

        var orchestrateTools = Assert.Single(activities, a => a.DisplayName == "orchestrate_tools");

        var executeTools = activities.Where(a => a.DisplayName.StartsWith("execute_tool")).ToList();
        Assert.NotEmpty(executeTools);
        foreach (var executeTool in executeTools)
        {
            Assert.Same(orchestrateTools, executeTool.Parent);
        }
    }

    [Fact]
    public async Task InformationalOnly_SetToTrueAfterProcessing()
    {
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Result 1")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        var chat = await InvokeAndAssertAsync(options, plan);

        // Find the FunctionCallContent in the chat history
        var functionCallMessage = chat.First(m => m.Contents.Any(c => c is FunctionCallContent));
        var functionCallContent = functionCallMessage.Contents.OfType<FunctionCallContent>().First();

        // Verify InformationalOnly was set to true after processing
        Assert.True(functionCallContent.InformationalOnly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InformationalOnly_IgnoresFunctionCallsWithInformationalOnlyTrue(bool streaming)
    {
        var functionInvokedCount = 0;
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => { functionInvokedCount++; return "Result 1"; }, "Func1")]
        };

        // Create a function call that has already been processed
        var alreadyProcessedFunctionCall = new FunctionCallContent("callId1", "Func1") { InformationalOnly = true };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                await Task.Yield();

                // Return a response with a FunctionCallContent that has InformationalOnly = true
                var message = new ChatMessage(ChatRole.Assistant, [alreadyProcessedFunctionCall]);
                return new ChatResponse(message);
            },
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                // Return a response with a FunctionCallContent that has InformationalOnly = true
                var message = new ChatMessage(ChatRole.Assistant, [alreadyProcessedFunctionCall]);
                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        ChatResponse response = streaming
            ? await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options).ToChatResponseAsync()
            : await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        // The function should not have been invoked since InformationalOnly was true
        Assert.Equal(0, functionInvokedCount);

        // The response should contain the FunctionCallContent but no FunctionResultContent
        Assert.Contains(response.Messages, m => m.Contents.Any(c => c is FunctionCallContent fcc && fcc.InformationalOnly));
        Assert.DoesNotContain(response.Messages, m => m.Contents.Any(c => c is FunctionResultContent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InformationalOnly_ProcessesMixedFunctionCalls(bool streaming)
    {
        var func1InvokedCount = 0;
        var func2InvokedCount = 0;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => { func1InvokedCount++; return "Result 1"; }, "Func1"),
                AIFunctionFactory.Create(() => { func2InvokedCount++; return "Result 2"; }, "Func2"),
            ]
        };

        // Create one function call that needs processing and one that doesn't
        var needsProcessing = new FunctionCallContent("callId1", "Func1") { InformationalOnly = false };
        var alreadyProcessed = new FunctionCallContent("callId2", "Func2") { InformationalOnly = true };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                await Task.Yield();

                if (contents.Count() == 1)
                {
                    // First call - return both function calls
                    var message = new ChatMessage(ChatRole.Assistant, [needsProcessing, alreadyProcessed]);
                    return new ChatResponse(message);
                }
                else
                {
                    // Second call - return final response after processing
                    var message = new ChatMessage(ChatRole.Assistant, "done");
                    return new ChatResponse(message);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                if (contents.Count() == 1)
                {
                    // First call - return both function calls
                    var message = new ChatMessage(ChatRole.Assistant, [needsProcessing, alreadyProcessed]);
                    return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
                }
                else
                {
                    // Second call - return final response after processing
                    var message = new ChatMessage(ChatRole.Assistant, "done");
                    return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        ChatResponse response = streaming
            ? await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options).ToChatResponseAsync()
            : await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        // Only Func1 should have been invoked (the one with InformationalOnly = false)
        Assert.Equal(1, func1InvokedCount);
        Assert.Equal(0, func2InvokedCount);

        // The response should contain FunctionResultContent for Func1 but not Func2
        Assert.Contains(response.Messages, m => m.Contents.Any(c => c is FunctionResultContent frc && frc.CallId == "callId1"));
        Assert.DoesNotContain(response.Messages, m => m.Contents.Any(c => c is FunctionResultContent frc && frc.CallId == "callId2"));
    }

    [Fact]
    public async Task InformationalOnly_MultipleFunctionInvokingChatClientsOnlyProcessOnce()
    {
        // Test that when multiple FunctionInvokingChatClients are in a pipeline,
        // each FunctionCallContent is only processed once (by the first one that sees it)
        var functionInvokedCount = 0;
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => { functionInvokedCount++; return "Result 1"; }, "Func1")]
        };

        var callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                await Task.Yield();

                // First call: return a FunctionCallContent that needs processing
                // Second call: return a final text response
                if (callCount++ == 0)
                {
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]));
                }
                else
                {
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"));
                }
            }
        };

        // Create a pipeline with two FunctionInvokingChatClients
        using var client1 = new FunctionInvokingChatClient(innerClient);
        using var client2 = new FunctionInvokingChatClient(client1);

        var response = await client2.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        // The function should have been invoked EXACTLY ONCE, not twice (once per FICC)
        Assert.Equal(1, functionInvokedCount);

        // The response should contain the FunctionCallContent with InformationalOnly = true
        Assert.Contains(response.Messages, m => m.Contents.Any(c => c is FunctionCallContent fcc && fcc.CallId == "callId1" && fcc.InformationalOnly));

        // There should be a FunctionResultContent since the function was processed
        Assert.Contains(response.Messages, m => m.Contents.Any(c => c is FunctionResultContent frc && frc.CallId == "callId1"));
    }

    private sealed class CustomSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                SetSynchronizationContext(this);
                d(state);
            });
        }
    }

    private static async Task<List<ChatMessage>> InvokeAndAssertAsync(
        ChatOptions? options,
        List<ChatMessage> plan,
        List<ChatMessage>? expected = null,
        Func<ChatClientBuilder, ChatClientBuilder>? configurePipeline = null,
        IServiceProvider? services = null)
    {
        Assert.NotEmpty(plan);

        configurePipeline ??= static b => b.UseFunctionInvocation();

        using CancellationTokenSource cts = new();
        List<ChatMessage> chat = [plan[0]];
        long expectedTotalTokenCounts = 0;

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                Assert.Equal(cts.Token, actualCancellationToken);

                await Task.Yield();

                var usage = CreateRandomUsage();
                expectedTotalTokenCounts += usage.InputTokenCount!.Value;

                var message = new ChatMessage(ChatRole.Assistant, CloneContents(plan[contents.Count()].Contents))
                {
                    MessageId = Guid.NewGuid().ToString("N")
                };
                return new ChatResponse(message) { Usage = usage };
            }
        };

        IChatClient service = configurePipeline(innerClient.AsBuilder()).Build(services);

        var result = await service.GetResponseAsync(new EnumeratedOnceEnumerable<ChatMessage>(chat), options, cts.Token);
        Assert.NotNull(result);

        chat.AddRange(result.Messages);

        expected ??= plan;
        AssertExtensions.EqualMessageLists(expected, chat);

        // Usage should be aggregated over all responses, including AdditionalUsage
        var actualUsage = result.Usage!;
        Assert.Equal(expectedTotalTokenCounts, actualUsage.InputTokenCount);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.OutputTokenCount);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.TotalTokenCount);
        Assert.Equal(2, actualUsage.AdditionalCounts!.Count);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.AdditionalCounts["firstValue"]);
        Assert.Equal(expectedTotalTokenCounts, actualUsage.AdditionalCounts["secondValue"]);

        return chat;
    }

    private static UsageDetails CreateRandomUsage()
    {
        // We'll set the same random number on all the properties so that, when determining the
        // correct sum in tests, we only have to total the values once
        var value = new Random().Next(100);
        return new UsageDetails
        {
            InputTokenCount = value,
            OutputTokenCount = value,
            TotalTokenCount = value,
            AdditionalCounts = new() { ["firstValue"] = value, ["secondValue"] = value },
        };
    }

    private static async Task<List<ChatMessage>> InvokeAndAssertStreamingAsync(
        ChatOptions? options,
        List<ChatMessage> plan,
        List<ChatMessage>? expected = null,
        Func<ChatClientBuilder, ChatClientBuilder>? configurePipeline = null,
        IServiceProvider? services = null)
    {
        Assert.NotEmpty(plan);

        configurePipeline ??= static b => b.UseFunctionInvocation();

        using CancellationTokenSource cts = new();
        List<ChatMessage> chat = [plan[0]];

        using var innerClient = new TestChatClient
        {
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                Assert.Equal(cts.Token, actualCancellationToken);

                ChatMessage message = new(ChatRole.Assistant, CloneContents(plan[contents.Count()].Contents))
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                };
                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        IChatClient service = configurePipeline(innerClient.AsBuilder()).Build(services);

        var result = await service.GetStreamingResponseAsync(new EnumeratedOnceEnumerable<ChatMessage>(chat), options, cts.Token).ToChatResponseAsync();
        Assert.NotNull(result);

        chat.AddRange(result.Messages);

        expected ??= plan;

        AssertExtensions.EqualMessageLists(expected, chat);
        return chat;
    }

    private static async IAsyncEnumerable<T> YieldAsync<T>(params IEnumerable<T> items)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            yield return item;
        }
    }

    /// <summary>
    /// Clones AIContent objects to avoid sharing mutable state across test invocations.
    /// </summary>
    private static List<AIContent> CloneContents(IList<AIContent> contents)
    {
        var cloned = new List<AIContent>(contents.Count);
        foreach (var content in contents)
        {
            // Clone FunctionCallContent to avoid sharing InformationalOnly state
            if (content is FunctionCallContent fcc)
            {
                cloned.Add(new FunctionCallContent(fcc.CallId, fcc.Name, fcc.Arguments)
                {
                    InformationalOnly = fcc.InformationalOnly,
                    Exception = fcc.Exception,
                    AdditionalProperties = fcc.AdditionalProperties,
                    Annotations = fcc.Annotations,
                    RawRepresentation = fcc.RawRepresentation
                });
            }
            else
            {
                // For other content types, just use the same reference
                cloned.Add(content);
            }
        }

        return cloned;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_AddTool(bool streaming)
    {
        // This test validates the scenario described in the issue:
        // 1. FunctionA is called
        // 2. FunctionA modifies ChatOptions.Tools by adding FunctionB
        // 3. The inner client returns a call to FunctionB
        // 4. FunctionB should be successfully invoked

        AIFunction functionB = AIFunctionFactory.Create(() => "FunctionB result", "FunctionB");
        bool functionBAdded = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Add FunctionB to ChatOptions.Tools during invocation
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Options!.Tools!.Add(functionB);
                functionBAdded = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    // First call - return FunctionA call
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    // Second call - after FunctionA modifies tools, return FunctionB call
                    Assert.True(functionBAdded, "FunctionA should have added FunctionB before second call");
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")])]);
                }
                else
                {
                    // Third call - return final response
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBAdded, "FunctionA should have added FunctionB before second call");
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>().Any(frc => frc.Result?.ToString() == "FunctionB result"));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>().Any(frc => frc.Result?.ToString() == "FunctionB result"));
        }

        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_RemoveTool(bool streaming)
    {
        // This test validates that removing a tool during function invocation is respected.
        // After FunctionA removes FunctionB, calls to FunctionB should result in "not found".

        AIFunction functionB = AIFunctionFactory.Create(() => "FunctionB result", "FunctionB");
        bool functionBRemoved = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Remove FunctionB from ChatOptions.Tools during invocation
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Options!.Tools!.Remove(functionB);
                functionBRemoved = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA, functionB]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    // First call - return FunctionA call
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    // Second call - after FunctionA removes FunctionB, still try to call it
                    Assert.True(functionBRemoved, "FunctionA should have removed FunctionB before second call");
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")])]);
                }
                else
                {
                    // Third call - return final response
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBRemoved, "FunctionA should have removed FunctionB before second call");
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();

            // FunctionB should result in "not found" error - the error message format is: "Error: Requested function \"FunctionB\" not found."
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString()?.Contains("FunctionB", StringComparison.Ordinal) == true &&
                            frc.Result?.ToString()?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);

            // FunctionB should result in "not found" error - the error message format is: "Error: Requested function \"FunctionB\" not found."
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString()?.Contains("FunctionB", StringComparison.Ordinal) == true &&
                            frc.Result?.ToString()?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true));
        }

        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_ReplaceTool(bool streaming)
    {
        // This test validates that replacing a tool during function invocation is respected.

        AIFunction originalFunctionB = AIFunctionFactory.Create(() => "Original FunctionB result", "FunctionB");
        AIFunction replacementFunctionB = AIFunctionFactory.Create(() => "Replacement FunctionB result", "FunctionB");
        bool functionBReplaced = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Replace FunctionB with a different implementation
                var context = FunctionInvokingChatClient.CurrentContext!;
                var tools = context.Options!.Tools!;
                int index = tools.IndexOf(originalFunctionB);

                // The original FunctionB should be in the tools list
                Assert.True(index >= 0, "originalFunctionB should be in the tools list");
                tools[index] = replacementFunctionB;
                functionBReplaced = true;

                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA, originalFunctionB]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBReplaced, "FunctionA should have replaced FunctionB before second call");
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBReplaced, "FunctionA should have replaced FunctionB before second call");
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();

            // Should use the replacement function, not the original
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Replacement FunctionB result"));
            Assert.DoesNotContain(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Original FunctionB result"));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Replacement FunctionB result"));
            Assert.DoesNotContain(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Original FunctionB result"));
        }

        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_AddToolWithAdditionalTools(bool streaming)
    {
        // This test validates the scenario when AdditionalTools are present and a tool is added to ChatOptions.Tools

        AIFunction additionalTool = AIFunctionFactory.Create(() => "AdditionalTool result", "AdditionalTool");
        AIFunction functionB = AIFunctionFactory.Create(() => "FunctionB result", "FunctionB");
        bool functionBAdded = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Options!.Tools!.Add(functionB);
                functionBAdded = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBAdded);

                    // Call both the newly added function and the additional tool
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB"),
                         new FunctionCallContent("callId3", "AdditionalTool")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBAdded);
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB"),
                         new FunctionCallContent("callId3", "AdditionalTool")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            AdditionalTools = [additionalTool]
        };

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "FunctionB result"));
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "AdditionalTool result"));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "FunctionB result"));
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "AdditionalTool result"));
        }

        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_AddToolOverridingAdditionalTool(bool streaming)
    {
        // This test validates that a tool added to ChatOptions.Tools takes precedence over an AdditionalTool with the same name

        AIFunction additionalToolSameName = AIFunctionFactory.Create(() => "AdditionalTool version", "SharedName");
        AIFunction addedToolSameName = AIFunctionFactory.Create(() => "Added version", "SharedName");
        bool toolAdded = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Add a tool with the same name as an additional tool - should override it
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Options!.Tools!.Add(addedToolSameName);
                toolAdded = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    Assert.True(toolAdded);
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "SharedName")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(toolAdded);
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "SharedName")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient)
        {
            AdditionalTools = [additionalToolSameName]
        };

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();

            // Should use the added tool, not the additional tool
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Added version"));
            Assert.DoesNotContain(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "AdditionalTool version"));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Added version"));
            Assert.DoesNotContain(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "AdditionalTool version"));
        }

        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolMapNotRefreshedWhenToolsUnchanged(bool streaming)
    {
        // This test validates that function invocation works correctly across multiple
        // iterations when tools haven't been modified

        int functionAInvocations = 0;
        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                functionAInvocations++;
                return $"FunctionA result {functionAInvocations}";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount <= 3)
                {
                    // Keep returning function calls
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent($"callId{callCount}", "FunctionA")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount <= 3)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent($"callId{callCount}", "FunctionA")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();
            Assert.Equal(3, functionAInvocations);
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);
            Assert.Equal(3, functionAInvocations);
        }

        Assert.Equal(4, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_ClearAllTools(bool streaming)
    {
        // This test validates that clearing all tools during function invocation is respected.

        AIFunction functionB = AIFunctionFactory.Create(() => "FunctionB result", "FunctionB");
        bool toolsCleared = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Clear all tools
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Options!.Tools!.Clear();
                toolsCleared = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA, functionB]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    Assert.True(toolsCleared);

                    // Try to call FunctionB after tools were cleared
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(toolsCleared);
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();

            // FunctionB should result in "not found" error - the error message format is: "Error: Requested function \"FunctionB\" not found."
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString()?.Contains("FunctionB", StringComparison.Ordinal) == true &&
                            frc.Result?.ToString()?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);

            // FunctionB should result in "not found" error - the error message format is: "Error: Requested function \"FunctionB\" not found."
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString()?.Contains("FunctionB", StringComparison.Ordinal) == true &&
                            frc.Result?.ToString()?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true));
        }

        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_AddApprovalRequiredTool(bool streaming)
    {
        // This test validates that adding an approval-required function during invocation is respected.
        // The added function should require approval on subsequent calls.

        AIFunction functionB = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "FunctionB result", "FunctionB"));
        bool functionBAdded = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Add an approval-required FunctionB during invocation
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Options!.Tools!.Add(functionB);
                functionBAdded = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBAdded, "FunctionA should have added FunctionB before second call");

                    // Try to call FunctionB - it should require approval
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBAdded, "FunctionA should have added FunctionB before second call");
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();

            // FunctionB should have been converted to an approval request (not executed)
            Assert.Contains(result.Messages, m => m.Contents.OfType<ToolApprovalRequestContent>().Any(frc => ((FunctionCallContent)frc.ToolCall).Name == "FunctionB"));

            // And FunctionA should have been executed
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>().Any(frc => frc.Result?.ToString() == "FunctionA result"));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);

            // FunctionB should have been converted to an approval request (not executed)
            Assert.Contains(result.Messages, m => m.Contents.OfType<ToolApprovalRequestContent>().Any(frc => ((FunctionCallContent)frc.ToolCall).Name == "FunctionB"));

            // And FunctionA should have been executed
            Assert.Contains(result.Messages, m => m.Contents.OfType<FunctionResultContent>().Any(frc => frc.Result?.ToString() == "FunctionA result"));
        }

        Assert.Equal(2, callCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespectsChatOptionsToolsModificationsByFunctionTool_ReplaceWithApprovalRequiredTool(bool streaming)
    {
        // This test validates that replacing a regular function with an approval-required function during invocation is respected.

        AIFunction originalFunctionB = AIFunctionFactory.Create(() => "Original FunctionB result", "FunctionB");
        AIFunction replacementFunctionB = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Replacement FunctionB result", "FunctionB"));
        bool functionBReplaced = false;

        AIFunction functionA = AIFunctionFactory.Create(
            () =>
            {
                // Replace FunctionB with an approval-required version
                var context = FunctionInvokingChatClient.CurrentContext!;
                var tools = context.Options!.Tools!;
                int index = tools.IndexOf(originalFunctionB);
                Assert.True(index >= 0, "originalFunctionB should be in the tools list");
                tools[index] = replacementFunctionB;
                functionBReplaced = true;
                return "FunctionA result";
            }, "FunctionA");

        var options = new ChatOptions
        {
            Tools = [functionA, originalFunctionB]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, chatOptions, ct) =>
            {
                await Task.Yield();
                callCount++;

                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")])]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBReplaced, "FunctionA should have replaced FunctionB before second call");

                    // Try to call FunctionB - it should now require approval
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")])]);
                }
                else
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
                }
            },
            GetStreamingResponseAsyncCallback = (contents, chatOptions, ct) =>
            {
                callCount++;

                ChatMessage message;
                if (callCount == 1)
                {
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId1", "FunctionA")]);
                }
                else if (callCount == 2)
                {
                    Assert.True(functionBReplaced, "FunctionA should have replaced FunctionB before second call");
                    message = new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("callId2", "FunctionB")]);
                }
                else
                {
                    message = new ChatMessage(ChatRole.Assistant, "Done");
                }

                return YieldAsync(new ChatResponse(message).ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        if (streaming)
        {
            var result = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")], options).ToChatResponseAsync();

            // FunctionB should have been converted to an approval request (not executed)
            Assert.Contains(result.Messages, m => m.Contents.OfType<ToolApprovalRequestContent>().Any(frc => ((FunctionCallContent)frc.ToolCall).Name == "FunctionB"));

            // Original FunctionB result should NOT be present
            Assert.DoesNotContain(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Original FunctionB result"));
        }
        else
        {
            var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options);

            // FunctionB should have been converted to an approval request (not executed)
            Assert.Contains(result.Messages, m => m.Contents.OfType<ToolApprovalRequestContent>().Any(frc => ((FunctionCallContent)frc.ToolCall).Name == "FunctionB"));

            // Original FunctionB result should NOT be present
            Assert.DoesNotContain(result.Messages, m => m.Contents.OfType<FunctionResultContent>()
                .Any(frc => frc.Result?.ToString() == "Original FunctionB result"));
        }

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task LogsFunctionNotFound()
    {
        var collector = new FakeLogCollector();
        ServiceCollection c = new();
        c.AddLogging(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "Result 1", "Func1")]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "UnknownFunc")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Error: Requested function \"UnknownFunc\" not found.")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b =>
            b.Use((c, services) => new FunctionInvokingChatClient(c, services.GetRequiredService<ILoggerFactory>()));

        await InvokeAndAssertAsync(options, plan, null, configure, c.BuildServiceProvider());

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Function 'UnknownFunc' not found") && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task LogsNonInvocableFunction()
    {
        var collector = new FakeLogCollector();
        ServiceCollection c = new();
        c.AddLogging(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var declarationOnly = AIFunctionFactory.Create(() => "Result 1", "Func1").AsDeclarationOnly();
        var options = new ChatOptions
        {
            Tools = [declarationOnly]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Should not be produced")]),
            new ChatMessage(ChatRole.Assistant, "world"),
        ];

        List<ChatMessage> expected = plan.Take(2).ToList();

        Func<ChatClientBuilder, ChatClientBuilder> configure = b =>
            b.Use((c, services) => new FunctionInvokingChatClient(c, services.GetRequiredService<ILoggerFactory>()));

        await InvokeAndAssertAsync(options, plan, expected, configure, c.BuildServiceProvider());

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Function 'Func1' is not invocable (declaration only)") && e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task LogsFunctionRequestedTermination()
    {
        var collector = new FakeLogCollector();
        ServiceCollection c = new();
        c.AddLogging(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() =>
            {
                var context = FunctionInvokingChatClient.CurrentContext!;
                context.Terminate = true;
                return "Terminated";
            }, "TerminatingFunc")]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "TerminatingFunc")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Terminated")]),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b =>
            b.Use((c, services) => new FunctionInvokingChatClient(c, services.GetRequiredService<ILoggerFactory>()));

        await InvokeAndAssertAsync(options, plan, null, configure, c.BuildServiceProvider());

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Function 'TerminatingFunc' requested termination of the processing loop") && e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task LogsFunctionRequestedTerminationWhenFunctionThrows()
    {
        var collector = new FakeLogCollector();
        ServiceCollection c = new();
        c.AddLogging(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(string () =>
            {
                FunctionInvokingChatClient.CurrentContext!.Terminate = true;
                throw new InvalidOperationException("Oh no!");
            }, "TerminatingFunc")]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "TerminatingFunc")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", result: "Error: Function failed.")]),
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b =>
            b.Use((c, services) => new FunctionInvokingChatClient(c, services.GetRequiredService<ILoggerFactory>()));

        await InvokeAndAssertAsync(options, plan, null, configure, c.BuildServiceProvider());

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Function 'TerminatingFunc' requested termination of the processing loop") && e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task LogsFunctionRequiresApproval()
    {
        var collector = new FakeLogCollector();
        ServiceCollection c = new();
        c.AddLogging(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var approvalFunc = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"));
        var options = new ChatOptions
        {
            Tools = [approvalFunc]
        };

        List<ChatMessage> plan =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
        ];

        // Expected output includes the user message and the approval request
        List<ChatMessage> expected =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1"))
            ])
        ];

        Func<ChatClientBuilder, ChatClientBuilder> configure = b =>
            b.Use((c, services) => new FunctionInvokingChatClient(c, services.GetRequiredService<ILoggerFactory>()));

        await InvokeAndAssertAsync(options, plan, expected, configure, c.BuildServiceProvider());

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Function 'Func1' requires approval") && e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task LogsProcessingApprovalResponse()
    {
        var collector = new FakeLogCollector();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var approvalFunc = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"));

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "world")))
        };

        using var client = new FunctionInvokingChatClient(innerClient, loggerFactory);

        var options = new ChatOptions { Tools = [approvalFunc] };

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1"))
            ]),
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", true, new FunctionCallContent("callId1", "Func1"))
            ])
        };

        await client.GetResponseAsync(messages, options);

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Processing approval response for 'Func1'. Approved: True") && e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task LogsFunctionRejected()
    {
        var collector = new FakeLogCollector();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)).SetMinimumLevel(LogLevel.Debug));

        var approvalFunc = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "Result 1", "Func1"));

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "world")))
        };

        using var client = new FunctionInvokingChatClient(innerClient, loggerFactory);

        var options = new ChatOptions { Tools = [approvalFunc] };

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "Func1"))
            ]),
            new ChatMessage(ChatRole.User,
            [
                new ToolApprovalResponseContent("ficc_callId1", false, new FunctionCallContent("callId1", "Func1")) { Reason = "User denied" }
            ])
        };

        await client.GetResponseAsync(messages, options);

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Function 'Func1' was rejected. Reason: User denied") && e.Level == LogLevel.Debug);
    }

    // Note: LogMaxConsecutiveErrorsExceeded is exercised by the existing
    // ContinuesWithFailingCallsUntilMaximumConsecutiveErrors test which triggers
    // the threshold condition. The logging call is at line 1078 and will execute
    // when MaximumConsecutiveErrorsPerRequest is exceeded.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerHandledFunctionCalls_MarkedAsInformationalOnly(bool streaming)
    {
        // When a server returns both a FunctionCallContent and matching FunctionResultContent,
        // the FCC should be automatically marked as InformationalOnly so it won't be invoked locally.
        var functionInvokedCount = 0;
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => { functionInvokedCount++; return "Result 1"; }, "Func1")]
        };

        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                await Task.Yield();
                return new ChatResponse(
                [
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
                    new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", "Server Result")]),
                ]);
            },
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                var response = new ChatResponse(
                [
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]),
                    new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", "Server Result")]),
                ]);
                return YieldAsync(response.ToChatResponseUpdates());
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        ChatResponse response = streaming
            ? await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options).ToChatResponseAsync()
            : await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        // The function should not have been invoked since the server already handled it
        Assert.Equal(0, functionInvokedCount);

        // The response should contain the FCC marked as InformationalOnly
        Assert.Contains(response.Messages, m =>
            m.Contents.Any(c => c is FunctionCallContent fcc2 && fcc2.CallId == "callId1" && fcc2.InformationalOnly));

        // The response should also contain the server's FRC
        Assert.Contains(response.Messages, m =>
            m.Contents.Any(c => c is FunctionResultContent frc2 && frc2.CallId == "callId1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerHandledFunctionCalls_MixedWithLocalCalls(bool streaming)
    {
        // When a server returns an FCC+FRC pair for one function and just an FCC for another,
        // only the locally-needed FCC should be invoked.
        var func1InvokedCount = 0;
        var func2InvokedCount = 0;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => { func1InvokedCount++; return "Local Result 1"; }, "Func1"),
                AIFunctionFactory.Create(() => { func2InvokedCount++; return "Local Result 2"; }, "Func2"),
            ]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                await Task.Yield();

                if (Interlocked.Increment(ref callCount) == 1)
                {
                    // First call: Func1 is server-handled (has both FCC and FRC), Func2 needs local invocation
                    return new ChatResponse(
                    [
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2")]),
                        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", "Server Result 1")]),
                    ]);
                }
                else
                {
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
                }
            },
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    var response = new ChatResponse(
                    [
                        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1"), new FunctionCallContent("callId2", "Func2")]),
                        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("callId1", "Server Result 1")]),
                    ]);
                    return YieldAsync(response.ToChatResponseUpdates());
                }
                else
                {
                    var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
                    return YieldAsync(response.ToChatResponseUpdates());
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        ChatResponse response = streaming
            ? await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options).ToChatResponseAsync()
            : await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        // Only Func2 should be invoked locally (Func1 was handled by server)
        Assert.Equal(0, func1InvokedCount);
        Assert.Equal(1, func2InvokedCount);

        // Func1's FCC should be marked InformationalOnly
        Assert.Contains(response.Messages, m =>
            m.Contents.Any(c => c is FunctionCallContent fcc2 && fcc2.CallId == "callId1" && fcc2.InformationalOnly));

        // There should be a FunctionResultContent for Func2 (locally invoked)
        Assert.Contains(response.Messages, m =>
            m.Contents.Any(c => c is FunctionResultContent frc2 && frc2.CallId == "callId2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerHandledFunctionCalls_NoMatchingFRC_StillInvoked(bool streaming)
    {
        // When a server returns a FCC without a matching FRC, it should still be invoked locally
        // (the existing behavior should be preserved).
        var functionInvokedCount = 0;
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => { functionInvokedCount++; return "Local Result"; }, "Func1")]
        };

        int callCount = 0;
        using var innerClient = new TestChatClient
        {
            GetResponseAsyncCallback = async (contents, actualOptions, actualCancellationToken) =>
            {
                await Task.Yield();

                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]));
                }
                else
                {
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
                }
            },
            GetStreamingResponseAsyncCallback = (contents, actualOptions, actualCancellationToken) =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("callId1", "Func1")]));
                    return YieldAsync(response.ToChatResponseUpdates());
                }
                else
                {
                    var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
                    return YieldAsync(response.ToChatResponseUpdates());
                }
            }
        };

        using var client = new FunctionInvokingChatClient(innerClient);

        ChatResponse response = streaming
            ? await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options).ToChatResponseAsync()
            : await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        // The function should have been invoked locally since there was no matching FRC
        Assert.Equal(1, functionInvokedCount);

        // There should be a FunctionResultContent from the local invocation
        Assert.Contains(response.Messages, m =>
            m.Contents.Any(c => c is FunctionResultContent frc2 && frc2.CallId == "callId1"));
    }
}

