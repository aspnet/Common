// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TestClasses;
using Xunit;

namespace Microsoft.Gen.Logging.Test;

public class LogMethodTests
{
    private sealed class ThrowingLogger : ILogger
    {
        public static ThrowingLogger Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("Provider failed.");
    }

    private sealed class CapturingLogger : ILogger
    {
        public object? RequestId { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> fields)
            {
                foreach (var field in fields)
                {
                    if (field.Key == "RequestId")
                    {
                        RequestId = field.Value;
                    }
                }
            }
        }
    }

    [Fact]
    public void BasicTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        NoNamespace.CouldNotOpenSocket(logger, "microsoft.com");
        Assert.Equal(LogLevel.Critical, collector.LatestRecord.Level);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("Could not open socket to `microsoft.com`", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        Level1.OneLevelNamespace.CouldNotOpenSocket(logger, "microsoft.com");
        Assert.Equal(LogLevel.Critical, collector.LatestRecord.Level);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("Could not open socket to `microsoft.com`", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        Level1.Level2.TwoLevelNamespace.CouldNotOpenSocket(logger, "microsoft.com");
        Assert.Equal(LogLevel.Critical, collector.LatestRecord.Level);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("Could not open socket to `microsoft.com`", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void FileScopedNamespaceTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        FileScopedNamespace.Log.CouldNotOpenSocket(logger, "microsoft.com");
        Assert.Equal(LogLevel.Critical, collector.LatestRecord.Level);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("Could not open socket to `microsoft.com`", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void EnableTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;
        var fakeLogger = logger.FakeLogger;

        fakeLogger.ControlLevel(LogLevel.Trace, false);
        fakeLogger.ControlLevel(LogLevel.Debug, false);
        fakeLogger.ControlLevel(LogLevel.Information, false);
        fakeLogger.ControlLevel(LogLevel.Warning, false);
        fakeLogger.ControlLevel(LogLevel.Error, false);
        fakeLogger.ControlLevel(LogLevel.Critical, false);

        LevelTestExtensions.M8(logger, LogLevel.Trace);
        LevelTestExtensions.M8(logger, LogLevel.Debug);
        LevelTestExtensions.M8(logger, LogLevel.Information);
        LevelTestExtensions.M8(logger, LogLevel.Warning);
        LevelTestExtensions.M8(logger, LogLevel.Error);
        LevelTestExtensions.M8(logger, LogLevel.Critical);

        Assert.Equal(0, collector.Count);
    }

    [Fact]
    public void OptionalArgTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        SignatureTestExtensions.M2(logger, "Hello");
        Assert.Equal(1, collector.Count);
        Assert.Equal("Hello World", collector.LatestRecord.Message);
        Assert.Equal(3, collector.LatestRecord.StructuredState!.Count);
        Assert.Equal("Hello", collector.LatestRecord.StructuredState!.GetValue("p1"));
        Assert.Equal("World", collector.LatestRecord.StructuredState!.GetValue("p2"));

        collector.Clear();
        SignatureTestExtensions.M2(logger, "Hello", "World");
        Assert.Equal(1, collector.Count);
        Assert.Equal("Hello World", collector.LatestRecord.Message);
        Assert.Equal(3, collector.LatestRecord.StructuredState!.Count);
        Assert.Equal("Hello", collector.LatestRecord.StructuredState!.GetValue("p1"));
        Assert.Equal("World", collector.LatestRecord.StructuredState!.GetValue("p2"));
    }

    [Fact]
    public void ArgTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        ArgTestExtensions.Method1(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M1", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method2(logger, "arg1");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M2 arg1", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method3(logger, "arg1", 2);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M3 arg1 2", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method4(logger, new InvalidOperationException("A"));
        Assert.Equal("A", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M4", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method5(logger, new InvalidOperationException("A"), new InvalidOperationException("B"));
        Assert.Equal("A", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M5 System.InvalidOperationException: B", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method6(logger, new InvalidOperationException("A"), 2);
        Assert.Equal("A", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M6 2", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method7(logger, 1, new InvalidOperationException("B"));
        Assert.Equal("B", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M7 1", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method8(logger, 1, 2, 3, 4, 5, 6, 7);
        Assert.Equal("M81234567", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method9(logger, 1, 2, 3, 4, 5, 6, 7);
        Assert.Equal("M9 1 2 3 4 5 6 7", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ArgTestExtensions.Method10(logger, 1);
        Assert.Equal("M101", collector.LatestRecord.Message);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void ThreadLocalStateIsClearedWhenLoggerThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ArgTestExtensions.Method11(ThrowingLogger.Instance, "stale-request", "alpha", "beta", "gamma"));

        var logger = new CapturingLogger();
        ArgTestExtensions.Method12(logger, "req-123");

        Assert.Equal("req-123", logger.RequestId);
    }

    [Fact]
    public void CollectionTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        CollectionTestExtensions.M0(logger);
        TestCollection(1, collector);

        collector.Clear();
        CollectionTestExtensions.M1(logger, 0);
        TestCollection(2, collector);

        collector.Clear();
        CollectionTestExtensions.M2(logger, 0, 1);
        TestCollection(3, collector);

        collector.Clear();
        CollectionTestExtensions.M3(logger, 0, 1, 2);
        TestCollection(4, collector);

        collector.Clear();
        CollectionTestExtensions.M4(logger, 0, 1, 2, 3);
        TestCollection(5, collector);

        collector.Clear();
        CollectionTestExtensions.M5(logger, 0, 1, 2, 3, 4);
        TestCollection(6, collector);

        collector.Clear();
        CollectionTestExtensions.M6(logger, 0, 1, 2, 3, 4, 5);
        TestCollection(7, collector);

        collector.Clear();
        CollectionTestExtensions.M7(logger, 0, 1, 2, 3, 4, 5, 6);
        TestCollection(8, collector);

        collector.Clear();
        CollectionTestExtensions.M8(logger, 0, 1, 2, 3, 4, 5, 6, 7);
        TestCollection(9, collector);

        collector.Clear();
        CollectionTestExtensions.M9(logger, LogLevel.Critical, 0, new ArgumentException("Foo"), 1);
        AssertLastState(collector, new("p0", "0"), new("p1", "1"));
    }

    [Fact]
    public void ConstructorVariationsTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        ConstructorVariationsTestExtensions.M0(logger, "Zero");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M0 Zero", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M1(logger, LogLevel.Error, "One");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M1 One", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M2(logger, "Two");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M3(logger, LogLevel.Error, "Three");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M4(logger, "Four");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M4 Four", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M5(logger, LogLevel.Error, "Five");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M5 Five", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M6(logger, "Six");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ConstructorVariationsTestExtensions.M7(logger, LogLevel.Information, "Seven");
        Assert.Equal(1, collector.Count);

        var logRecord = collector.LatestRecord;
        Assert.Null(logRecord.Exception);
        Assert.Equal(string.Empty, logRecord.Message);
        Assert.Equal(LogLevel.Information, logRecord.Level);
        Assert.NotEqual(0, logRecord.Id.Id);
        Assert.Equal("M7", logRecord.Id.Name);

        collector.Clear();
        ConstructorVariationsTestExtensions.M8(logger, "Seven");
        Assert.Equal(1, collector.Count);

        logRecord = collector.LatestRecord;
        Assert.Null(logRecord.Exception);
        Assert.Equal("M8", logRecord.Message);
        Assert.Equal(LogLevel.Trace, logRecord.Level);
        Assert.Equal(42, logRecord.Id.Id);
        Assert.Equal("EN1", logRecord.Id.Name);
    }

    [Fact]
    public void MessageTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        MessageTestExtensions.M0(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Trace, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        MessageTestExtensions.M1(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        MessageTestExtensions.M2(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        MessageTestExtensions.M5(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("\"Hello\" World", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        MessageTestExtensions.M6(logger, LogLevel.Warning, "p", "q");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("\"p\" -> \"q\"", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Warning, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        MessageTestExtensions.M7(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("\"\n\r\\", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.NotEqual(0, collector.LatestRecord.Id.Id);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void InstanceTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        var o = new TestInstances(logger);

        collector.Clear();
        o.M0();
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M0", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        o.M1("Foo");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M1 Foo", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Trace, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        o.M2(LogLevel.Warning, "param");
        Assert.Equal(1, collector.Count);

        var logRecord = collector.LatestRecord;
        Assert.Null(logRecord.Exception);
        Assert.Equal(string.Empty, logRecord.Message);
        Assert.Equal(LogLevel.Warning, logRecord.Level);
        Assert.NotNull(logRecord.StructuredState);
        Assert.Single(logRecord.StructuredState!);
        Assert.Equal("p1", logRecord.StructuredState![0].Key);
        Assert.Equal("param", logRecord.StructuredState[0].Value);
    }

    [Fact]
    public void LevelTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        LevelTestExtensions.M0(logger);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M1(logger);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M2(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M2", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Information, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M3(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M3", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Warning, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M4(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M4", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M5(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M5", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Critical, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M6(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M6", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.None, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M7(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M7", collector.LatestRecord.Message);
        Assert.Equal((LogLevel)42, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M8(logger, LogLevel.Critical);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M8", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Critical, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M9(LogLevel.Information, logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M9", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Information, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M10(logger, LogLevel.Warning);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M10 Warning", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Warning, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        LevelTestExtensions.M11(logger, LogLevel.Error);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void LevelTests_ForNonStatic()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        var redactorProvider = new SimpleRedactorProvider();
        var instance = new NonStaticTestClass(logger);

        instance.NoParams();
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("No params here...", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Warning, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        instance.NoParamsWithLevel(LogLevel.Information);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("No params here as well...", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Information, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        instance.NoParamsWithLevel(LogLevel.Error);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("No params here as well...", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void NonStaticNullable()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        var instance = new NonStaticNullableTestClass(logger);
        instance.M2("One", "Two", "Three");
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M2 --- --- -----", collector.LatestRecord.Message);

        collector.Clear();
        instance = new NonStaticNullableTestClass(null);
        instance.M2("One", "Two", "Three");
        Assert.Equal(0, collector.Count);
    }

    [Fact]
    public void ExceptionTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        ExceptionTestExtensions.M0(logger, new ArgumentException("Foo"), new ArgumentException("Bar"));
        Assert.Equal("Foo", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M0 System.ArgumentException: Bar", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Trace, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ExceptionTestExtensions.M1(new ArgumentException("Foo"), logger, new ArgumentException("Bar"));
        Assert.Equal("Foo", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M1 System.ArgumentException: Bar", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        ExceptionTestExtensions.M2(logger, "One", new ArgumentException("Foo"));
        Assert.Equal("Foo", collector.LatestRecord.Exception!.Message);
        Assert.Equal("M2 One: System.ArgumentException: Foo", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        var exception = new ArgumentException("Foo");
        ExceptionTestExtensions.M3(exception, logger, LogLevel.Error);
        Assert.Equal(1, collector.Count);
        Assert.NotNull(collector.LatestRecord.Exception);
        Assert.Equal(exception.Message, collector.LatestRecord.Exception!.Message);
        Assert.Equal(exception.GetType(), collector.LatestRecord.Exception!.GetType());
        Assert.Equal(string.Empty, collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
    }

    [Fact]
    public void EventNameTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        EventNameTestExtensions.M0(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M0", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Trace, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);
        Assert.Equal("CustomEventName", collector.LatestRecord.Id.Name);

        collector.Clear();
        EventNameTestExtensions.M1(LogLevel.Warning, logger, "Eight");
        Assert.Equal(1, collector.Count);

        var logRecord = collector.LatestRecord;
        Assert.Null(logRecord.Exception);
        Assert.Equal(string.Empty, logRecord.Message);
        Assert.Equal(LogLevel.Warning, logRecord.Level);
        Assert.NotEqual(0, logRecord.Id.Id);
        Assert.Equal("M1_Event", logRecord.Id.Name);
    }

    [Fact]
    public void EventIdTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        EventNameTestExtensions.M1(LogLevel.Warning, logger, "Eight");
        Assert.Equal(1, collector.Count);

        var firstEventId = collector.LatestRecord.Id;
        Assert.NotEqual(0, firstEventId.Id);
        Assert.Equal("M1_Event", firstEventId.Name);

        collector.Clear();
        EventNameTestExtensions.M1_Event(logger, "Nine");
        Assert.Equal(1, collector.Count);

        var secondEventId = collector.LatestRecord.Id;
        Assert.Equal(firstEventId.Id, secondEventId.Id); // Same EventName means same generated EventId
        Assert.Equal(nameof(EventNameTestExtensions.M1_Event), secondEventId.Name);

        collector.Clear();
        EventNameTestExtensions.M2(logger, "Ten");
        Assert.Equal(1, collector.Count);

        var thirdEventId = collector.LatestRecord.Id;
        Assert.NotEqual(thirdEventId.Id, secondEventId.Id); // Different EventName means different generated EventId
        Assert.Equal(nameof(EventNameTestExtensions.M2), thirdEventId.Name);
    }

    [Fact]
    public void NestedClassTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        NestedClassTestExtensions<Alien.Abc>.NestedMiddleParentClass.NestedClass.M8(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M8", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        NonStaticNestedClassTestExtensions<Alien.Abc>.NonStaticNestedMiddleParentClass.NestedClass.M9(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M9", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        NestedStruct.Logger.M10(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M10", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        NestedRecord.Logger.M11(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M11", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);

        collector.Clear();
        MultiLevelNestedClass.NestedStruct.NestedRecord.Logger.M12(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M12", collector.LatestRecord.Message);
        Assert.Equal(LogLevel.Debug, collector.LatestRecord.Level);
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void TemplateTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        TemplateTestExtensions.M0(logger, 0);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M0 0", collector.LatestRecord.Message);
        AssertLastState(collector,
            new KeyValuePair<string, string?>("A1", "0"),
            new KeyValuePair<string, string?>("{OriginalFormat}", "M0 {A1}"));

        collector.Clear();
        TemplateTestExtensions.M1(logger, 42);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M1 42 42", collector.LatestRecord.Message);
        AssertLastState(collector,
            new KeyValuePair<string, string?>("A1", "42"),
            new KeyValuePair<string, string?>("{OriginalFormat}", "M1 {A1} {A1}"));

        collector.Clear();
        TemplateTestExtensions.M2(logger, 42, 43, 44, 45, 46, 47, 48);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M2 42 43 44 45 46 47 48", collector.LatestRecord.Message);
        AssertLastState(collector,
            new KeyValuePair<string, string?>("A1", "42"),
            new KeyValuePair<string, string?>("a2", "43"),
            new KeyValuePair<string, string?>("A3", "44"),
            new KeyValuePair<string, string?>("a4", "45"),
            new KeyValuePair<string, string?>("A5", "46"),
            new KeyValuePair<string, string?>("a6", "47"),
            new KeyValuePair<string, string?>("A7", "48"),
            new KeyValuePair<string, string?>("{OriginalFormat}", "M2 {A1} {a2} {A3} {a4} {A5} {a6} {A7}"));

        collector.Clear();
        TemplateTestExtensions.M3(logger, 42, 43);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M3 43 42", collector.LatestRecord.Message);
        AssertLastState(collector,
            new KeyValuePair<string, string?>("A1", "42"),
            new KeyValuePair<string, string?>("a2", "43"),
            new KeyValuePair<string, string?>("{OriginalFormat}", "M3 {a2} {A1}"));
    }

    [Fact]
    public void StructTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        StructTestExtensions.M0(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M0", collector.LatestRecord.Message);
        AssertLastState(collector,
            new KeyValuePair<string, string?>("{OriginalFormat}", "M0"));
    }

    [Fact]
    public void RecordTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        collector.Clear();
        RecordTestExtensions.M0(logger);
        Assert.Null(collector.LatestRecord.Exception);
        Assert.Equal("M0", collector.LatestRecord.Message);
        AssertLastState(collector,
            new KeyValuePair<string, string?>("{OriginalFormat}", "M0"));
    }

    [Fact]
    public void SkipEnabledCheckTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;
        var fakeLogger = logger.FakeLogger;

        fakeLogger.ControlLevel(LogLevel.Information, false);

        SkipEnabledCheckTestExtensions.LoggerMethodWithFalseSkipEnabledCheck(logger);
        Assert.Equal(0, collector.Count);

        SkipEnabledCheckTestExtensions.LoggerMethodWithFalseSkipEnabledCheck(logger, LogLevel.Information, "p1");
        Assert.Equal(0, collector.Count);

#if NET6_0_OR_GREATER
        SkipEnabledCheckTestExtensions.LoggerMethodWithTrueSkipEnabledCheck(logger);
        Assert.Equal(1, collector.Count);
#endif
    }

    [Fact]
    public void InParameterTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        InParameterTestExtensions.S s;
        InParameterTestExtensions.M0(logger, in s);
        Assert.Equal(1, collector.Count);
        Assert.Contains("Hello from S", collector.LatestRecord.Message);
    }

    [Fact]
    public void RefReadOnlyParameterTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        RefReadOnlyParameterTestExtensions.S s;
        RefReadOnlyParameterTestExtensions.M0(logger, ref s);
        Assert.Equal(1, collector.Count);
        Assert.Contains("Hello from S", collector.LatestRecord.Message);
    }

    [Fact]
    public void AtSymbolsTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        AtSymbolsTestExtensions.M0(logger, "Test");
        var record = Assert.Single(collector.GetSnapshot());
        Assert.Equal("M0 Test", record.Message);
        Assert.NotNull(record.StructuredState);
        Assert.Contains(record.StructuredState, x => x.Key == "event");
        Assert.DoesNotContain(record.StructuredState, x => x.Key == "@event");

        AtSymbolsTestExtensions.M1(logger, "Test");
        Assert.Equal(2, collector.Count);

        var o = new AtSymbolsTestExtensions.SpecialNames
        {
            @class = 42,
        };

        collector.Clear();
        AtSymbolsTestExtensions.UseAtSymbol3(logger, LogLevel.Debug, "42", 42);
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("@myevent2"));
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("otherevent"));
        Assert.Equal("UseAtSymbol3, 42 42", collector.LatestRecord.Message);

        collector.Clear();
        AtSymbolsTestExtensions.UseAtSymbol4(logger, LogLevel.Debug, "42", 42, new ArgumentException("Foo"));
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("@myevent3"));
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("otherevent"));
        Assert.Equal("UseAtSymbol4 with error, 42 42", collector.LatestRecord.Message);
        Assert.NotNull(collector.LatestRecord.Exception);

        collector.Clear();
        AtSymbolsTestExtensions.M3(logger, LogLevel.Debug, o);
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("event.class"));

        collector.Clear();
        AtSymbolsTestExtensions.M5(logger, LogLevel.Debug, o);
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("class"));

        collector.Clear();
        AtSymbolsTestExtensions.M6(logger, "42");
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("class"));
        Assert.Equal("M6 class 42", collector.LatestRecord.Message);

        collector.Clear();
        AtSymbolsTestExtensions.M7(logger, "42");
        Assert.Equal("42", collector.LatestRecord.StructuredState!.GetValue("@param"));
        Assert.Equal("M7 param 42", collector.LatestRecord.Message);
    }

    [Fact]
    public void OverloadsTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        OverloadsTestExtensions.M0(logger, "Test");
        OverloadsTestExtensions.M0(logger, 42);
        Assert.Equal(2, collector.Count);
    }

    [Fact]
    public void NullableTests()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        NullableTestExtensions.M0(logger, null);
        Assert.Equal("M0 (null)", collector.LatestRecord.Message);

        NullableTestExtensions.M1(logger, null);
        Assert.Equal("M1 (null)", collector.LatestRecord.Message);

        NullableTestExtensions.M3(logger, null);
        Assert.Equal("M3 (null)", collector.LatestRecord.Message);

        NullableTestExtensions.M4(logger, null, null, null, null, null, null, null, null, null);
        Assert.Equal("M4 (null) (null) (null) (null) (null) (null) (null) (null) (null)", collector.LatestRecord.Message);

        NullableTestExtensions.M5(logger, null, null, null, null, null, null, null, null, null);
        Assert.Equal("M5 (null) (null) (null) (null) (null) (null) (null) (null) (null)", collector.LatestRecord.Message);

        collector.Clear();
        NullableTestExtensions.M6(null, "Nothing");
        Assert.Equal(0, collector.Count);
    }

    [Fact]
    public void InvariantTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        var dt = new DateTime(2022, 5, 22);

        var oldCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");

        InvariantTestExtensions.M0(logger, dt);
        Assert.Equal(dt.ToString(CultureInfo.InvariantCulture), collector.LatestRecord.StructuredState!.GetValue("p0"));
        Assert.Equal("M0 " + dt.ToString(CultureInfo.InvariantCulture), collector.LatestRecord.Message);

        Thread.CurrentThread.CurrentCulture = oldCulture;
    }

    [Fact]
    public void EnumerableTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        EnumerableTestExtensions.M10(logger,
            new[] { 1, 2, 3 },
            new[] { 4, 5, 6 },
            new Dictionary<string, int>
            {
                { "Seven", 7 },
                { "Eight", 8 },
                { "Nine", 9 }
            });

        Assert.Equal(1, collector.Count);

        Assert.Equal("[\"1\",\"2\",\"3\"]", collector.LatestRecord.StructuredState!.GetValue("p1"));
        Assert.Equal("[\"4\",\"5\",\"6\"]", collector.LatestRecord.StructuredState!.GetValue("p2"));
        Assert.Equal("{\"Seven\"=\"7\",\"Eight\"=\"8\",\"Nine\"=\"9\"}", collector.LatestRecord.StructuredState!.GetValue("p3"));
    }

    [Fact]
    public void NullableEnumerableTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        EnumerableTestExtensions.M11(logger, null);
        Assert.Equal(1, collector.Count);
        Assert.Null(collector.LatestRecord.StructuredState!.GetValue("p1"));

        collector.Clear();
        EnumerableTestExtensions.M11(logger, new[] { 1, 2, 3 });
        Assert.Equal(1, collector.Count);
        Assert.Equal("[\"1\",\"2\",\"3\"]", collector.LatestRecord.StructuredState!.GetValue("p1"));

        collector.Clear();
        EnumerableTestExtensions.M12(logger, null);
        Assert.Equal(1, collector.Count);
        Assert.Null(collector.LatestRecord.StructuredState!.GetValue("class"));

        collector.Clear();
        EnumerableTestExtensions.M12(logger, new[] { 1, 2, 3 });
        Assert.Equal(1, collector.Count);
        Assert.Equal("[\"1\",\"2\",\"3\"]", collector.LatestRecord.StructuredState!.GetValue("class"));

        collector.Clear();
        EnumerableTestExtensions.M13(logger, default);
        Assert.Equal(1, collector.Count);
        Assert.Equal("[\"1\",\"2\",\"3\"]", collector.LatestRecord.StructuredState!.GetValue("p1"));

        collector.Clear();
#pragma warning disable SA1129 // Do not use default value type constructor
        EnumerableTestExtensions.M14(logger, new StructEnumerable());
#pragma warning restore SA1129 // Do not use default value type constructor
        Assert.Equal(1, collector.Count);
        Assert.Equal("[\"1\",\"2\",\"3\"]", collector.LatestRecord.StructuredState!.GetValue("p1"));

        collector.Clear();
        EnumerableTestExtensions.M14(logger, default);
        Assert.Equal(1, collector.Count);
        Assert.Null(collector.LatestRecord.StructuredState!.GetValue("p1"));
    }

    [Fact]
    public void FormattableTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        FormattableTestExtensions.Method1(logger, default);
        Assert.Equal(1, collector.Count);
        Assert.Equal("Formatted!", collector.LatestRecord.StructuredState!.GetValue("p1"));

        collector.Clear();
        FormattableTestExtensions.Method2(logger, new FormattableTestExtensions.ComplexObj());
        Assert.Equal(1, collector.Count);
        Assert.Equal("Formatted!", collector.LatestRecord.StructuredState!.GetValue("p1.P1"));

        collector.Clear();
        FormattableTestExtensions.Method3(logger, default);
        Assert.Equal(1, collector.Count);
        Assert.Equal("Converted!", collector.LatestRecord.StructuredState!.GetValue("p1"));
    }

    [Fact]
    public void OriginalFormatTest()
    {
        using var logger = Utils.GetLogger();
        var collector = logger.FakeLogCollector;

        OriginalFormatTestExtensions.M0(logger);
        Assert.Equal(1, collector.Count);
        EnsureOriginalFormatIsLastProperty(collector.LatestRecord, "M0", "M0");

        collector.Clear();
        OriginalFormatTestExtensions.M1(logger, "0");
        Assert.Equal(1, collector.Count);
        EnsureOriginalFormatIsLastProperty(collector.LatestRecord, "M1 {p0}", "M1 0");

        collector.Clear();
        OriginalFormatTestExtensions.M2(logger, "0", "1");
        Assert.Equal(1, collector.Count);
        EnsureOriginalFormatIsLastProperty(collector.LatestRecord, "M2 {p0}{p1}", "M2 01");

        collector.Clear();
        OriginalFormatTestExtensions.M3(logger, "0", "1", "2");
        Assert.Equal(1, collector.Count);
        Assert.Empty(collector.LatestRecord.Message);
        Assert.Equal(3, collector.LatestRecord.StructuredState!.Count);
        AssertLastState(collector, new("p0", "0"), new("p1", "1"), new("p2", "2"));
    }

    private static void AssertLastState(FakeLogCollector collector, params KeyValuePair<string, string?>[] expected)
    {
        var rol = (IReadOnlyList<KeyValuePair<string, string>>)collector.LatestRecord.State!;

        foreach (var kvp in expected)
        {
            Assert.Equal(kvp.Value, rol.GetValue(kvp.Key));
        }
    }

    [SuppressMessage("Minor Code Smell", "S4056:Overloads with a \"CultureInfo\" or an \"IFormatProvider\" parameter should be used", Justification = "Not appropriate here")]
    private static void TestCollection(int expected, FakeLogCollector collector)
    {
        var rol = (collector.LatestRecord.State as IReadOnlyList<KeyValuePair<string, string?>>)!;
        Assert.NotNull(rol);

        Assert.Equal(expected, rol.Count);
        for (int i = 0; i < expected; i++)
        {
            if (i != expected - 1)
            {
                var kvp = new KeyValuePair<string, string?>($"p{i}", i.ToString());
                Assert.Equal(kvp.Value, rol!.GetValue(kvp.Key));
            }
        }
    }

    private static void EnsureOriginalFormatIsLastProperty(FakeLogRecord record, string format, string message)
    {
        var state = (IReadOnlyList<KeyValuePair<string, string>>)record.State!;
        Assert.NotNull(state);

        var pair = state.Last();
        Assert.Equal("{OriginalFormat}", pair.Key);
        Assert.Equal(format, pair.Value);
        Assert.Equal(message, record.Message);
    }
}
