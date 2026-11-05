// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Testing;
using TestClasses;
using Xunit;

namespace Microsoft.Gen.Logging.Test;

public class TransitiveTests
{
    [Fact]
    public void Basic()
    {
        var logger = new FakeLogger();
        var c = new TransitiveTestExtensions.C0();

        TransitiveTestExtensions.M0(logger, c);

        var expectedState = new Dictionary<string, string?>
        {
            ["p0.P1"] = "V1",
            ["p0.P0.P2"] = "V2",
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);

        TransitiveTestExtensions.M1(logger, c);

        expectedState = new Dictionary<string, string?>
        {
            ["p0.P1"] = "V1",
            ["p0.P0"] = "TS1",
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);
    }

    [Fact]
    public void Indexers()
    {
        var logger = new FakeLogger();

        TransitiveTestExtensions.M2(logger, new());

        // Ensure that we don't enumerate indexers in transitive logging:
        var expectedState = new Dictionary<string, string?>
        {
            ["p0.P3.Capacity"] = "0",
            ["p0.P3.Count"] = "0",
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);

        TransitiveTestExtensions.M3(logger, new() { P4 = 42 });

        // No indexers here as well:
        expectedState = new Dictionary<string, string?>
        {
            ["p0.P4"] = "42",
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);
    }

    [Fact]
    public void SystemTypeIsLoggedAsSingleValue()
    {
        var logger = new FakeLogger();

        TransitiveTestExtensions.M4(logger, new());

        var expectedState = new Dictionary<string, string?>
        {
            ["p0.Kind"] = typeof(TransitiveTestExtensions.C4).ToString(),
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);
    }

    [Fact]
    public void IgnoredAndDelegatePropertiesAreNotLogged()
    {
        var logger = new FakeLogger();

        TransitiveTestExtensions.M5(logger, new());

        var expectedState = new Dictionary<string, string?>
        {
            ["p0.P5"] = "V5",
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);
    }

    [Fact]
    public void TransitiveOnPropertyIsHonored()
    {
        var logger = new FakeLogger();

        TransitiveTestExtensions.M6(logger, new());

        var expectedState = new Dictionary<string, string?>
        {
            ["p0.P8.P1"] = "V1",
            ["p0.P8.P0.P2"] = "V2",
        };

        logger.Collector.LatestRecord.StructuredState.Should().NotBeNull().And.Equal(expectedState);
    }
}
