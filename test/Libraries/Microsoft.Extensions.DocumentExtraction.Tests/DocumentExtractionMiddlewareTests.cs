// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction.Tests;

public sealed class DocumentExtractionMiddlewareTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UseLogging_ResolvesFactoryIndependentlyForEachBuild(bool firstWithoutServices)
    {
        FakeLogCollector firstCollector = new();
        FakeLogCollector secondCollector = new();
        using TestLoggerFactory firstFactory = new(firstCollector);
        using TestLoggerFactory secondFactory = new(secondCollector);
        using ServiceProvider firstServices = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(firstFactory).BuildServiceProvider();
        using ServiceProvider secondServices = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(secondFactory).BuildServiceProvider();
        ExtractionDocument expected = new("document");
        DocumentExtractionClientBuilder builder = new(_ => new TestClient(expected));
        builder.UseLogging();

        using DocumentExtractionClient first = builder.Build(firstWithoutServices ? null : firstServices);
        using DocumentExtractionClient second = builder.Build(secondServices);
        if (firstWithoutServices)
        {
            Assert.IsType<TestClient>(first);
        }
        else
        {
            Assert.IsType<LoggingDocumentExtractionClient>(first);
        }

        Assert.IsType<LoggingDocumentExtractionClient>(second);
        using MemoryStream input = new();
        Assert.Same(expected, await first.ExtractAsync(input, "first", "application/pdf"));
        Assert.Same(expected, await second.ExtractAsync(input, "second", "application/pdf"));
        Assert.Equal(firstWithoutServices ? 0 : 2, firstCollector.Count);
        Assert.Equal(2, secondCollector.Count);
        AssertLifecycle(secondCollector, "completed");
    }

    [Fact]
    public async Task UseLogging_ExplicitFactoryOverridesEachServiceProvider()
    {
        FakeLogCollector explicitCollector = new();
        FakeLogCollector serviceCollector = new();
        using TestLoggerFactory explicitFactory = new(explicitCollector);
        using TestLoggerFactory serviceFactory = new(serviceCollector);
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(serviceFactory).BuildServiceProvider();
        ExtractionDocument expected = new("document");
        DocumentExtractionClientBuilder builder = new(_ => new TestClient(expected));
        builder.UseLogging(explicitFactory);
        using DocumentExtractionClient first = builder.Build();
        using DocumentExtractionClient second = builder.Build(services);
        using MemoryStream input = new();
        Assert.Same(expected, await first.ExtractAsync(input, "first", "application/pdf"));
        Assert.Same(expected, await second.ExtractAsync(input, "second", "application/pdf"));
        Assert.Equal(4, explicitCollector.Count);
        Assert.Equal(0, serviceCollector.Count);
        Assert.Equal(2, explicitCollector.GetSnapshot().Count(static entry => entry.Message == "Document extraction completed."));
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped, false)]
    [InlineData(ServiceLifetime.Transient, false)]
    [InlineData(ServiceLifetime.Scoped, true)]
    [InlineData(ServiceLifetime.Transient, true)]
    public void FactoryRegistration_ScopesOwnIndependentClients(ServiceLifetime lifetime, bool keyed)
    {
        List<TestClient> clients = [];
        ServiceCollection services = new();
        DocumentExtractionClient Factory(IServiceProvider _)
        {
            TestClient client = new();
            clients.Add(client);
            return client;
        }

        DocumentExtractionClientBuilder builder = keyed
            ? services.AddKeyedDocumentExtractionClient("key", Factory, lifetime)
            : services.AddDocumentExtractionClient(Factory, lifetime);
        builder.Use(inner => new RecordingMiddleware(inner, () => { }));
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();
        DocumentExtractionClient Resolve(IServiceProvider scopeServices) => keyed
            ? scopeServices.GetRequiredKeyedService<DocumentExtractionClient>("key")
            : scopeServices.GetRequiredService<DocumentExtractionClient>();
        DocumentExtractionClient first = Resolve(firstScope.ServiceProvider);
        DocumentExtractionClient repeated = Resolve(firstScope.ServiceProvider);
        DocumentExtractionClient second = Resolve(secondScope.ServiceProvider);
        Assert.NotSame(first, second);
        if (lifetime == ServiceLifetime.Scoped)
        {
            Assert.Same(first, repeated);
            Assert.Equal(2, clients.Count);
        }
        else
        {
            Assert.NotSame(first, repeated);
            Assert.Equal(3, clients.Count);
        }

        TestClient secondInner = clients[clients.Count - 1];
        Assert.All(clients, client => Assert.Equal(0, client.DisposeCount));
        firstScope.Dispose();
        Assert.All(clients.Take(clients.Count - 1), client => Assert.Equal(1, client.DisposeCount));
        Assert.Equal(0, secondInner.DisposeCount);
        secondScope.Dispose();
        Assert.All(clients, client => Assert.Equal(1, client.DisposeCount));
    }

    [Fact]
    public void BuilderAndDependencyInjection_PreserveConfiguredLifetimeAndMiddlewareOrder()
    {
        using TestClient inner = new();
        DocumentExtractionClientBuilder builder = new(inner);
        List<string> disposed = [];
        builder.Use((client, _) => new RecordingMiddleware(client, () => disposed.Add("first")));
        builder.Use((client, _) => new RecordingMiddleware(client, () => disposed.Add("second")));
        using (DocumentExtractionClient client = builder.Build())
        {
            Assert.IsType<RecordingMiddleware>(client);
            Assert.IsType<RecordingMiddleware>(((RecordingMiddleware)client).Inner);
        }

        Assert.Equal(new[] { "first", "second" }, disposed);
        Assert.Equal(1, inner.DisposeCount);
        ServiceCollection services = new();
        using TestClient singletonClient = new();
        services.AddDocumentExtractionClient(singletonClient);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();
        Assert.Same(singletonClient, firstScope.ServiceProvider.GetRequiredService<DocumentExtractionClient>());
        Assert.Same(singletonClient, secondScope.ServiceProvider.GetRequiredService<DocumentExtractionClient>());
        firstScope.Dispose();
        secondScope.Dispose();
        Assert.Equal(0, singletonClient.DisposeCount);
        provider.Dispose();
        Assert.Equal(1, singletonClient.DisposeCount);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData((ServiceLifetime)(-1))]
    public void InstanceRegistration_RejectsUnsupportedLifetimeBeforeChangingServices(ServiceLifetime lifetime)
    {
        using TestClient inner = new();
        ServiceCollection services = new();
        services.AddSingleton(new FakeLogger());
        ServiceDescriptor[] before = services.ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(nameof(lifetime), () => services.AddDocumentExtractionClient(inner, lifetime));

        Assert.Equal(before, services);
        Assert.Equal(0, inner.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuilderFailure_PreservesPrimaryBeforeCleanupFailure(bool nullFactory)
    {
        using TestClient inner = new();
        IOException primary = new("Factory failed.");
        IOException cleanup = new("Cleanup failed.");
        DocumentExtractionClientBuilder builder = new(inner);
        builder.Use(_ => nullFactory ? null! : throw primary);
        builder.Use(client => new RecordingMiddleware(client, () => throw cleanup));

        AggregateException actual = Assert.Throws<AggregateException>(() => builder.Build());

        Assert.Equal(2, actual.InnerExceptions.Count);
        if (nullFactory)
        {
            Assert.Equal("Middleware factory at index 0 returned null.",
                Assert.IsType<InvalidOperationException>(actual.InnerExceptions[0]).Message);
        }
        else
        {
            Assert.Same(primary, actual.InnerExceptions[0]);
        }

        Assert.Same(cleanup, actual.InnerExceptions[1]);
        Assert.Equal(1, inner.DisposeCount);
    }

    [Fact]
    public void BuilderFailure_DisposesPartiallyBuiltPipeline()
    {
        using TestClient inner = new();
        List<string> disposed = [];
        DocumentExtractionClientBuilder builder = new(inner);
        InvalidOperationException failure = new("Factory failed.");
        builder.Use(_ => throw failure);
        builder.Use(client => new RecordingMiddleware(client, () => disposed.Add("wrapped")));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => builder.Build()));
        Assert.Equal(new[] { "wrapped" }, disposed);
        Assert.Equal(1, inner.DisposeCount);
        Assert.Throws<ArgumentNullException>(() => new DocumentExtractionClientBuilder((DocumentExtractionClient)null!));
    }

    [Fact]
    public void KeyedRegistration_ForwardsServicesAndOwnsPipeline()
    {
        using TestClient inner = new();
        ServiceCollection services = new();
        services.AddSingleton(new FakeLogger());
        services.AddKeyedDocumentExtractionClient("key", provider =>
        {
            Assert.NotNull(provider.GetRequiredService<FakeLogger>());
            return inner;
        }).Use(client => new RecordingMiddleware(client, () => { }));
        using ServiceProvider provider = services.BuildServiceProvider();
        DocumentExtractionClient client = provider.GetRequiredKeyedService<DocumentExtractionClient>("key");
        Assert.Same(client, provider.GetRequiredKeyedService<DocumentExtractionClient>("key"));
        Assert.Same(client, client.GetService(typeof(RecordingMiddleware)));
        Assert.Null(client.GetService(typeof(RecordingMiddleware), "different"));
        provider.Dispose();
        Assert.Equal(1, inner.DisposeCount);
    }

    [Fact]
    public async Task Logging_AwaitsDocumentAndLogsOnlyExactLifecycle()
    {
        using MemoryStream input = new([42]);
        using Native native = new();
        using CountingStream body = new(OriginalBytes());
        ExtractionDocument expected = new("secret/path") { RawRepresentation = new { Native = native, Body = body } };
        ExtractionDocumentSection section = new();
        section.Elements.Add(new ExtractionDocumentImageElement(new TextContent("visible **text**"), "visible text"));
        expected.Sections.Add(section);
        using SemaphoreSlim gate = new(0, 1);
        using TestClient inner = new(expected)
        {
            Extract = async () =>
            {
                await gate.WaitAsync();
                return expected;
            }
        };
        FakeLogCollector logs = new();
        using LoggingDocumentExtractionClient client = new(inner, new FakeLogger(logs));
        DocumentExtractionOptions options = new() { ExtractorName = "OPTIONS_ONLY_93" };
        options.AdditionalProperties.Add("native", native);
        Task<ExtractionDocument> pending = client.ExtractAsync(input, "secret/path", "application/pdf", options);
        Assert.False(pending.IsCompleted);
        Assert.Equal(new[] { "Document extraction invoked." }, logs.GetSnapshot().Select(static entry => entry.Message));
        gate.Release();

        ExtractionDocument actual = await pending;

        Assert.Same(expected, actual);
        Assert.Equal("visible **text**", Assert.IsType<TextContent>(Assert.Single(actual.EnumerateContent()).Content).Text);
        Assert.Equal(OriginalBytes(), body.ToArray());
        AssertLifecycle(logs, "completed");
        client.Dispose();
        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(0, body.DisposeCount);
        body.Dispose();
        Assert.Equal(1, body.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Logging_FailureAndCancellationPreservePrimaryWithoutPayloads(bool cancel, bool synchronous)
    {
        using MemoryStream input = new([42]);
        using CancellationTokenSource cancellation = new();
        Exception primary = cancel
            ? new OperationCanceledException("SECRET_EXCEPTION_PAYLOAD", cancellation.Token)
            : new IOException("SECRET_EXCEPTION_PAYLOAD");
        using TestClient inner = new()
        {
            Extract = () => synchronous ? throw primary : Task.FromException<ExtractionDocument>(primary)
        };
        FakeLogCollector logs = new();
        using LoggingDocumentExtractionClient client = new(inner, new FakeLogger(logs));
        Exception actual = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ExtractAsync(input, "secret/path", "application/pdf", cancellationToken: cancellation.Token));
        Assert.Same(primary, actual);
        Assert.Equal(cancellation.Token, inner.Token);
        Assert.Same(input, inner.Source);
        Assert.Equal(1, inner.CallCount);
        if (cancel)
        {
            Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
        }

        AssertLifecycle(logs, cancel ? "canceled" : "failed");
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task Logging_SnapshotsOptionsBeforeReturningWithoutMutatingCaller()
    {
        using MemoryStream input = new([42]);
        using Native native = new();
        using CancellationTokenSource cancellation = new();
        ExtractionDocument expected = new("document");
        using SemaphoreSlim gate = new(0, 1);
        using TestClient inner = new(expected)
        {
            Extract = async () =>
            {
                await gate.WaitAsync();
                return expected;
            }
        };
        FakeLogCollector logs = new();
        using LoggingDocumentExtractionClient client = new(inner, new FakeLogger(logs));
        DocumentExtractionOptions options = new() { ExtractorName = "OPTIONS_ONLY_93" };
        options.AdditionalProperties.Add("native", native);
        Task<ExtractionDocument> pending = client.ExtractAsync(input, "secret/path", "application/pdf", options, cancellation.Token);
        DocumentExtractionOptions forwarded = Assert.IsType<DocumentExtractionOptions>(inner.Options);
        Assert.NotSame(options, forwarded);
        Assert.NotSame(options.AdditionalProperties, forwarded.AdditionalProperties);
        Assert.Equal("OPTIONS_ONLY_93", forwarded.ExtractorName);
        Assert.Same(native, forwarded.AdditionalProperties["native"]);
        Assert.Same(input, inner.Source);
        Assert.Equal("secret/path", inner.Identifier);
        Assert.Equal("application/pdf", inner.MediaType);
        Assert.Equal(cancellation.Token, inner.Token);
        options.ExtractorName = "caller changed";
        options.AdditionalProperties.Clear();
        Assert.Equal("OPTIONS_ONLY_93", forwarded.ExtractorName);
        forwarded.ExtractorName = "provider changed";
        forwarded.AdditionalProperties.Clear();
        Assert.Equal("caller changed", options.ExtractorName);
        Assert.Empty(options.AdditionalProperties);
        gate.Release();
        Assert.Same(expected, await pending);
        AssertLifecycle(logs, "completed");
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task Logging_NativeRootAndSourcesPreserveIdentityWithoutLeaks()
    {
        using MemoryStream input = new([42]);
        using Native native = new();
        ExtractionDocument expected = new("document") { RawRepresentation = native };
        ExtractionDocumentSourceReference reference = new(new ExtractionDocumentSourcePage("frame", 7), rawRepresentation: native);
        ExtractionDocumentImageElement image = new(new TextContent("visible **text**"), "visible text");
        image.Sources.Add(reference);
        ExtractionDocumentSection section = new();
        section.Elements.Add(image);
        expected.Sections.Add(section);
        expected.SourcePages.Add(reference.Page);
        using TestClient inner = new(expected);
        FakeLogCollector logs = new();
        using LoggingDocumentExtractionClient client = new(inner, new FakeLogger(logs));

        ExtractionDocument actual = await client.ExtractAsync(input, "secret/path", "application/pdf");

        Assert.Same(expected, actual);
        Assert.Null(inner.Options);
        Assert.Same(reference, Assert.Single(image.Sources));
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Same(native, actual.RawRepresentation);
        Assert.Same(image, Assert.Single(actual.EnumerateContent()));
        Assert.Equal("visible **text**", Assert.IsType<TextContent>(image.Content).Text);
        AssertLifecycle(logs, "completed");
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Fact]
    public async Task Logging_CompletionLogFailurePreservesPrimaryAndBorrowedData()
    {
        using MemoryStream input = new([42]);
        using Native native = new();
        IOException logFailure = new("completion logger failed");
        ExtractionDocument unreturned = new("secret/path") { RawRepresentation = native };
        using TestClient inner = new(unreturned);
        ThrowingLogger logger = new("Document extraction completed.", logFailure);
        using LoggingDocumentExtractionClient client = new(inner, logger);

        Exception actual = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ExtractAsync(input, "secret/path", "application/pdf", new DocumentExtractionOptions { }));

        Assert.Same(logFailure, actual);
        Assert.Same(native, unreturned.RawRepresentation);

        Assert.Equal(new[] { "Document extraction invoked.", "Document extraction completed." }, logger.Messages);
        Assert.Equal(1, inner.CallCount);
        client.Dispose();
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Logging_FailureLogFailurePreservesProviderPrimaryAndCancellationToken(bool cancel, bool synchronous)
    {
        using MemoryStream input = new([42]);
        using Native native = new();
        using CancellationTokenSource cancellation = new();
        Exception primary = cancel
            ? new OperationCanceledException("SECRET_EXCEPTION_PAYLOAD", cancellation.Token)
            : new IOException("SECRET_EXCEPTION_PAYLOAD");
        IOException loggingFailure = new("terminal logger failed");
        string terminal = cancel ? "Document extraction canceled." : "Document extraction failed.";
        ThrowingLogger logger = new(terminal, loggingFailure);
        using TestClient inner = new()
        {
            Extract = () => synchronous ? throw primary : Task.FromException<ExtractionDocument>(primary)
        };
        using LoggingDocumentExtractionClient client = new(inner, logger);
        DocumentExtractionOptions options = new();
        options.AdditionalProperties.Add("native", native);

        AggregateException actual = await Assert.ThrowsAsync<AggregateException>(() =>
            client.ExtractAsync(input, "secret/path", "application/pdf", options, cancellation.Token));

        Assert.Equal(new[] { primary, loggingFailure }, actual.InnerExceptions);
        Assert.Same(primary, actual.InnerExceptions[0]);
        Assert.Equal(new[] { "Document extraction invoked.", terminal }, logger.Messages);
        Assert.Equal(cancellation.Token, inner.Token);
        Assert.Same(input, inner.Source);
        Assert.Equal(1, inner.CallCount);
        if (cancel)
        {
            Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(actual.InnerExceptions[0]).CancellationToken);
        }

        Assert.Same(native, options.AdditionalProperties["native"]);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoryRegistration_SingletonSharesOneOwnedPipelineAcrossScopes(bool keyed)
    {
        ServiceCollection services = new();
        List<TestClient> terminals = [];
        IServiceProvider? factoryServices = null;
        int middlewareDisposals = 0;
        DocumentExtractionClient Factory(IServiceProvider serviceProvider)
        {
            factoryServices = serviceProvider;
            TestClient terminal = new();
            terminals.Add(terminal);
            return terminal;
        }

        DocumentExtractionClientBuilder builder = keyed
            ? services.AddKeyedDocumentExtractionClient("singleton-key", Factory, ServiceLifetime.Singleton)
            : services.AddDocumentExtractionClient(Factory, ServiceLifetime.Singleton);
        builder.Use(inner => new RecordingMiddleware(inner, () => middlewareDisposals++));
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();
        DocumentExtractionClient Resolve(IServiceProvider scopeServices) => keyed
            ? scopeServices.GetRequiredKeyedService<DocumentExtractionClient>("singleton-key")
            : scopeServices.GetRequiredService<DocumentExtractionClient>();

        DocumentExtractionClient first = Resolve(firstScope.ServiceProvider);
        Assert.Same(first, Resolve(firstScope.ServiceProvider));
        Assert.Same(first, Resolve(secondScope.ServiceProvider));
        Assert.Same(first, Resolve(secondScope.ServiceProvider));
        Assert.Same(first, Resolve(provider));
        TestClient terminal = Assert.Single(terminals);
        Assert.Same(terminal, Assert.IsType<RecordingMiddleware>(first).Inner);
        Assert.Same(provider.GetRequiredService<IServiceProvider>(), factoryServices);
        Assert.Equal(0, terminal.DisposeCount);
        Assert.Equal(0, middlewareDisposals);

        firstScope.Dispose();
        secondScope.Dispose();
        Assert.Equal(0, terminal.DisposeCount);
        Assert.Equal(0, middlewareDisposals);
        Assert.Same(first, Resolve(provider));
        Assert.Single(terminals);
        provider.Dispose();
        Assert.Equal(1, middlewareDisposals);
        Assert.Equal(1, terminal.DisposeCount);
    }

    [Fact]
    public async Task BuiltLoggingPipeline_PreservesExactExtractionOrderAndNativeKeyedLookup()
    {
        using MemoryStream input = new([42, 43]);
        using Native native = new();
        using CountingStream body = new(OriginalBytes());
        using CancellationTokenSource cancellation = new();
        ExtractionDocument expected = new("secret/path", new TextContent("visible **text**")) { RawRepresentation = native };
        ExtractionDocumentSourceReference reference = new(new ExtractionDocumentSourcePage("frame", 7), rawRepresentation: native);
        ExtractionDocumentImageElement image = new(new TextContent("visible text"), "visible text");
        image.Sources.Add(reference);
        ExtractionDocumentSection section = new();
        section.Elements.Add(image);
        expected.Sections.Add(section);
        expected.SourcePages.Add(reference.Page);
        List<string> events = [];
        object supportedKey = new();
        object unsupportedKey = new();
        using PipelineTerminal terminal = new(native, supportedKey, events);
        FakeLogCollector logs = new();
        using TestLoggerFactory factory = new(logs);
        ExtractionRecordingMiddleware? secondRecorder = null;
        DocumentExtractionClientBuilder builder = new(terminal);
        builder.Use(inner => new ExtractionRecordingMiddleware(inner, "first", events));
        builder.UseLogging(factory);
        builder.Use(inner => secondRecorder = new ExtractionRecordingMiddleware(inner, "second", events));
        using DocumentExtractionClient pipeline = builder.Build();
        ExtractionRecordingMiddleware first = Assert.IsType<ExtractionRecordingMiddleware>(pipeline);
        Assert.IsType<LoggingDocumentExtractionClient>(first.Inner);
        ExtractionRecordingMiddleware second = Assert.IsType<ExtractionRecordingMiddleware>(secondRecorder);
        Assert.Same(terminal, second.Inner);

        Assert.Same(pipeline, pipeline.GetService(typeof(DocumentExtractionClient)));
        Assert.Same(pipeline, pipeline.GetService(typeof(object)));
        Assert.Same(native, pipeline.GetService(typeof(Native)));
        Assert.Same(native, pipeline.GetService(typeof(Native), supportedKey));
        Assert.Null(pipeline.GetService(typeof(Native), unsupportedKey));
        Assert.Null(pipeline.GetService(typeof(Stream), supportedKey));

        void AssertLookups()
        {
            Assert.Equal(4, terminal.Lookups.Count);
            Assert.Equal(new[] { typeof(Native), typeof(Native), typeof(Native), typeof(Stream) },
                terminal.Lookups.Select(static lookup => lookup.serviceType));
            Assert.Null(terminal.Lookups[0].serviceKey);
            Assert.Same(supportedKey, terminal.Lookups[1].serviceKey);
            Assert.Same(unsupportedKey, terminal.Lookups[2].serviceKey);
            Assert.Same(supportedKey, terminal.Lookups[3].serviceKey);
        }

        AssertLookups();
        Assert.Equal(0, terminal.CallCount);
        Assert.Empty(events);
        Assert.Equal(0, logs.Count);
        Assert.Equal(0, terminal.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        DocumentExtractionOptions options = new() { ExtractorName = "OPTIONS_ONLY_93" };
        options.AdditionalProperties.Add("native", native);
        options.AdditionalProperties.Add("body", body);
        options.AdditionalProperties.Add("Setting", null);

        Task<ExtractionDocument> pending = pipeline.ExtractAsync(input, "secret/path", "application/pdf", options, cancellation.Token);

        Assert.False(pending.IsCompleted);
        Assert.Equal(new[] { "first-enter", "second-enter", "terminal" }, events);
        Assert.Equal(new[] { "Document extraction invoked." }, logs.GetSnapshot().Select(static entry => entry.Message));
        Assert.Equal(1, terminal.CallCount);
        Assert.Same(input, first.Source);
        Assert.Same(input, second.Source);
        Assert.Same(input, terminal.Source);
        Assert.Equal("secret/path", first.Identifier);
        Assert.Equal("secret/path", second.Identifier);
        Assert.Equal("secret/path", terminal.Identifier);
        Assert.Equal("application/pdf", first.MediaType);
        Assert.Equal("application/pdf", second.MediaType);
        Assert.Equal("application/pdf", terminal.MediaType);
        Assert.Equal(cancellation.Token, first.Token);
        Assert.Equal(cancellation.Token, second.Token);
        Assert.Equal(cancellation.Token, terminal.Token);
        Assert.Same(options, first.Options);
        DocumentExtractionOptions secondOptions = Assert.IsType<DocumentExtractionOptions>(second.Options);
        DocumentExtractionOptions terminalOptions = Assert.IsType<DocumentExtractionOptions>(terminal.Options);
        Assert.NotSame(options, secondOptions);
        Assert.NotSame(options, terminalOptions);
        Assert.NotSame(secondOptions, terminalOptions);
        Assert.NotSame(options.AdditionalProperties, secondOptions.AdditionalProperties);
        Assert.NotSame(options.AdditionalProperties, terminalOptions.AdditionalProperties);
        Assert.NotSame(secondOptions.AdditionalProperties, terminalOptions.AdditionalProperties);
        foreach (DocumentExtractionOptions snapshot in new[] { options, secondOptions, terminalOptions })
        {
            Assert.Equal("OPTIONS_ONLY_93", snapshot.ExtractorName);
            Assert.Equal(3, snapshot.AdditionalProperties.Count);
            Assert.Same(native, snapshot.AdditionalProperties["native"]);
            Assert.Same(body, snapshot.AdditionalProperties["body"]);
            Assert.True(snapshot.AdditionalProperties.TryGetValue("Setting", out object? presentNull));
            Assert.Null(presentNull);
            Assert.False(snapshot.AdditionalProperties.ContainsKey("SETTING"));
        }

        options.ExtractorName = "caller changed";
        options.AdditionalProperties.Clear();
        Assert.Equal("OPTIONS_ONLY_93", secondOptions.ExtractorName);
        Assert.Equal("OPTIONS_ONLY_93", terminalOptions.ExtractorName);
        Assert.Equal(3, secondOptions.AdditionalProperties.Count);
        Assert.Equal(3, terminalOptions.AdditionalProperties.Count);
        terminalOptions.ExtractorName = "terminal changed";
        terminalOptions.AdditionalProperties.Clear();
        Assert.Equal("OPTIONS_ONLY_93", secondOptions.ExtractorName);
        Assert.Same(native, secondOptions.AdditionalProperties["native"]);
        Assert.Same(body, secondOptions.AdditionalProperties["body"]);
        Assert.Equal("caller changed", options.ExtractorName);
        Assert.Empty(options.AdditionalProperties);
        terminal.SetResult(expected);

        ExtractionDocument actual = await pending;

        Assert.Same(expected, actual);
        Assert.Equal(new[] { "first-enter", "second-enter", "terminal", "second-exit", "first-exit" }, events);
        Assert.Equal(5, events.Count);
        Assert.All(new[] { "first-enter", "second-enter", "terminal", "second-exit", "first-exit" },
            entry => Assert.Equal(1, events.Count(recorded => recorded == entry)));
        Assert.Equal(1, terminal.CallCount);
        AssertLifecycle(logs, "completed");
        AssertLookups();
        pipeline.Dispose();
        pipeline.Dispose();
        Assert.Equal(1, terminal.DisposeCount);
        Assert.Same(native, actual.RawRepresentation);
        Assert.Same(image, Assert.Single(actual.EnumerateContent()));
        Assert.Same(reference, Assert.Single(image.Sources));
        Assert.Same(native, reference.GetRawRepresentation());
        Assert.Equal("visible **text**", Assert.IsType<TextContent>(actual.Content).Text);
        Assert.Equal("visible text", Assert.IsType<TextContent>(image.Content).Text);
        Assert.Equal(OriginalBytes(), body.ToArray());
        Assert.Equal(0, body.DisposeCount);
        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, native.ToStringCount);
        Assert.Equal(42, input.ReadByte());
    }

    private sealed class ExtractionRecordingMiddleware(DocumentExtractionClient inner, string name, List<string> events)
        : DelegatingDocumentExtractionClient(inner)
    {
        internal DocumentExtractionClient Inner => InnerClient;
        internal Stream? Source { get; private set; }
        internal string? Identifier { get; private set; }
        internal string? MediaType { get; private set; }
        internal DocumentExtractionOptions? Options { get; private set; }
        internal CancellationToken Token { get; private set; }

        public override async Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Source = document;
            Identifier = identifier;
            MediaType = mediaType;
            Options = options;
            Token = cancellationToken;
            events.Add(name + "-enter");
            ExtractionDocument result = await base.ExtractAsync(document, identifier, mediaType, options, cancellationToken);
            events.Add(name + "-exit");
            return result;
        }
    }

    private sealed class PipelineTerminal(Native native, object supportedKey, List<string> events)
        : DocumentExtractionClient
    {
        private TaskCompletionSource<ExtractionDocument>? _completion;

        internal List<(Type serviceType, object? serviceKey)> Lookups { get; } = [];
        internal Stream? Source { get; private set; }
        internal string? Identifier { get; private set; }
        internal string? MediaType { get; private set; }
        internal DocumentExtractionOptions? Options { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal int CallCount { get; private set; }
        internal int DisposeCount { get; private set; }

        internal void SetResult(ExtractionDocument result) => _completion!.SetResult(result);

        public override Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Source = document;
            Identifier = identifier;
            MediaType = mediaType;
            Options = options;
            Token = cancellationToken;
            CallCount++;
            events.Add("terminal");
            TaskCompletionSource<ExtractionDocument> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = completion;
            return completion.Task;
        }

        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            Lookups.Add((serviceType, serviceKey));
            return serviceType == typeof(Native) && (serviceKey is null || ReferenceEquals(supportedKey, serviceKey))
                ? native
                : base.GetService(serviceType, serviceKey);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingLogger(string terminal, Exception failure) : ILogger
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            string message = formatter(state, exception);
            Messages.Add(message);
            if (message == terminal)
            {
                throw failure;
            }
        }
    }

    private static void AssertLifecycle(FakeLogCollector collector, string ending)
    {
        var entries = collector.GetSnapshot();
        Assert.Equal(new[] { "Document extraction invoked.", "Document extraction " + ending + "." },
            entries.Select(static entry => entry.Message));
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            Assert.Null(entry.Exception);
            Assert.Equal(ending == "failed" && i == 1 ? LogLevel.Error : LogLevel.Debug, entry.Level);
            var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, string?>>>(entry.StructuredState);
            KeyValuePair<string, string?> originalFormat = Assert.Single(state);
            Assert.Equal("{OriginalFormat}", originalFormat.Key);
            Assert.Equal(entry.Message, originalFormat.Value);
            foreach (string marker in new[] { "RAW_BODY_ONLY_91", "NATIVE_ONLY_92", "OPTIONS_ONLY_93", "secret/path", "visible text", "visible **text**", "SECRET_EXCEPTION_PAYLOAD" })
            {
                Assert.DoesNotContain(marker, entry.Message);
                Assert.DoesNotContain(marker, originalFormat.Value ?? "");
            }
        }
    }

    private static byte[] OriginalBytes() => System.Text.Encoding.UTF8.GetBytes(
        " {\r\n \"unknown\" : {\"z\":[3,1]}, \"marker\":\"RAW_BODY_ONLY_91\" \r\n}\n");

    private sealed class TestClient(ExtractionDocument? result = null) : DocumentExtractionClient
    {
        internal Func<Task<ExtractionDocument>>? Extract { get; set; }
        internal Stream? Source { get; private set; }
        internal string? Identifier { get; private set; }
        internal string? MediaType { get; private set; }
        internal DocumentExtractionOptions? Options { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal int CallCount { get; private set; }
        internal int DisposeCount { get; private set; }
        public override Task<ExtractionDocument> ExtractAsync(Stream document, string identifier, string mediaType,
            DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Source = document;
            Identifier = identifier;
            MediaType = mediaType;
            Options = options;
            Token = cancellationToken;
            CallCount++;
            if (Extract is { } extract)
            {
                return extract();
            }

            return result is { } actual ? Task.FromResult(actual) :
                Task.FromException<ExtractionDocument>(new InvalidOperationException("No fake result configured."));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingMiddleware(DocumentExtractionClient inner, Action disposed) : DelegatingDocumentExtractionClient(inner)
    {
        internal DocumentExtractionClient Inner => InnerClient;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    disposed();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }

    private sealed class TestLoggerFactory(FakeLogCollector collector) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new FakeLogger(collector, categoryName);
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class Native : IDisposable
    {
        internal int DisposeCount { get; private set; }
        internal int ToStringCount { get; private set; }
        public void Dispose() => DisposeCount++;
        public override string ToString()
        {
            ToStringCount++;
            return "NATIVE_ONLY_92";
        }
    }

    private sealed class CountingStream(byte[] bytes, Exception? failure = null) : MemoryStream(bytes)
    {
        internal int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
            if (disposing && failure is not null)
            {
                throw failure;
            }
        }
    }
}
