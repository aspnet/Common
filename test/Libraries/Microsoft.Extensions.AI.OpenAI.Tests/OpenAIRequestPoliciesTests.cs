// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using OpenAI;
using Xunit;

#pragma warning disable OPENAI001 // Experimental OpenAI APIs
#pragma warning disable MEAI001  // OpenAIRequestPolicies is experimental

namespace Microsoft.Extensions.AI;

public class OpenAIRequestPoliciesTests
{
    [Fact]
    public void AddPolicy_NullPolicy_Throws()
    {
        var policies = new OpenAIRequestPolicies();
        Assert.Throws<ArgumentNullException>("policy", () => policies.AddPolicy(null!));
    }

    [Fact]
    public void GetService_OpenAIChatClient_ReturnsStableInstance()
    {
        IChatClient client = NewChatClient();

        var first = client.GetService<OpenAIRequestPolicies>();
        var second = client.GetService<OpenAIRequestPolicies>();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetService_OpenAIResponseClient_ReturnsInstance()
    {
        IChatClient client = new OpenAIClient(new ApiKeyCredential("k")).GetResponsesClient().AsIChatClient("m");
        Assert.NotNull(client.GetService<OpenAIRequestPolicies>());
    }

    [Fact]
    public void GetService_OpenAIEmbeddingGenerator_ReturnsInstance()
    {
        IEmbeddingGenerator<string, Embedding<float>> generator =
            new OpenAIClient(new ApiKeyCredential("k")).GetEmbeddingClient("m").AsIEmbeddingGenerator();

        Assert.NotNull(generator.GetService<OpenAIRequestPolicies>());
    }

    [Fact]
    public void GetService_PerClientIsolation()
    {
        var openAi = new OpenAIClient(new ApiKeyCredential("k"));

        var policiesA = openAi.GetChatClient("m").AsIChatClient().GetService<OpenAIRequestPolicies>();
        var policiesB = openAi.GetChatClient("m").AsIChatClient().GetService<OpenAIRequestPolicies>();

        Assert.NotSame(policiesA, policiesB);
    }

    [Fact]
    public void GetService_ReachableThroughDecoratorChain()
    {
        IChatClient inner = NewChatClient();
        var innerPolicies = inner.GetService<OpenAIRequestPolicies>();

        using IChatClient pipeline = inner
            .AsBuilder()
            .UseFunctionInvocation()
            .UseDistributedCache(new MemoryDistributedCache(Options.Options.Create(new MemoryDistributedCacheOptions())))
            .Build();

        Assert.Same(innerPolicies, pipeline.GetService<OpenAIRequestPolicies>());
    }

    [Fact]
    public async Task AddPolicy_CustomUserAgent_ReplacesMeaiHeader()
    {
        using var handler = new CapturingUserAgentHandler();
        using var http = new HttpClient(handler);
        IChatClient client = NewChatClient(http);

        client.GetService<OpenAIRequestPolicies>()!.AddPolicy(new SetUserAgentPolicy("my-sdk/1.0"));

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync("hi"));

        // Customer policy ran after MEAI's UA policy and replaced the value.
        Assert.NotNull(handler.CapturedUserAgent);
        Assert.Equal("my-sdk/1.0", handler.CapturedUserAgent);
    }

    [Fact]
    public async Task AddPolicy_CustomHeaderAdd_StacksWithMeaiUserAgent()
    {
        using var handler = new CapturingUserAgentHandler();
        using var http = new HttpClient(handler);
        IChatClient client = NewChatClient(http);

        client.GetService<OpenAIRequestPolicies>()!.AddPolicy(new AddUserAgentPolicy("extra-sdk/9.9"));

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync("hi"));

        Assert.NotNull(handler.CapturedUserAgent);
        Assert.Contains("MEAI", handler.CapturedUserAgent);
        Assert.Contains("extra-sdk/9.9", handler.CapturedUserAgent);
    }

    [Fact]
    public async Task NoPolicyRegistered_MeaiUserAgentStillEmitted()
    {
        using var handler = new CapturingUserAgentHandler();
        using var http = new HttpClient(handler);
        IChatClient client = NewChatClient(http);

        Assert.NotNull(client.GetService<OpenAIRequestPolicies>()); // touch but don't register

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync("hi"));

        Assert.NotNull(handler.CapturedUserAgent);
        Assert.Contains("MEAI", handler.CapturedUserAgent);
    }

    [Fact]
    public async Task AddPolicy_Concurrent_AllPoliciesRetained()
    {
        var policies = new OpenAIRequestPolicies();

        const int Count = 200;
        await Task.WhenAll(Enumerable.Range(0, Count).Select(i =>
            Task.Run(() => policies.AddPolicy(new NoopPolicy()))));

        // Verify nothing was lost across CAS races.
        var entries = (Array)typeof(OpenAIRequestPolicies)
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(policies)!;
        Assert.Equal(Count, entries.Length);
    }

    private static IChatClient NewChatClient(HttpClient? http = null)
    {
        OpenAIClientOptions options = http is null
            ? new OpenAIClientOptions()
            : new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) };

        return new OpenAIClient(new ApiKeyCredential("k"), options)
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient();
    }

    private sealed class CapturingUserAgentHandler : HttpMessageHandler
    {
        public string? CapturedUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            // Capture the User-Agent values exactly as they appear on the outgoing request.
            CapturedUserAgent = request.Headers.UserAgent.ToString();

            // Short-circuit; the test only cares about what was sent.
            throw new InvalidOperationException("captured");
        }
    }

    private sealed class NoopPolicy : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }

    private sealed class SetUserAgentPolicy : PipelinePolicy
    {
        private readonly string _value;

        public SetUserAgentPolicy(string value)
        {
            _value = value;
        }

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("User-Agent", _value);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("User-Agent", _value);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }

    private sealed class AddUserAgentPolicy : PipelinePolicy
    {
        private readonly string _value;

        public AddUserAgentPolicy(string value)
        {
            _value = value;
        }

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Add("User-Agent", _value);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Add("User-Agent", _value);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
