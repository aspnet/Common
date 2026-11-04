// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Diagnostics.Buffering.Test;

public class LogBufferingFilterRuleTests
{
    [Fact]
    public void SelectsRightRule()
    {
        // Arrange
        var rules = new List<LogBufferingFilterRule>
        {
            new LogBufferingFilterRule(),
            new LogBufferingFilterRule(eventId: 1),
            new LogBufferingFilterRule(logLevel: LogLevel.Information, eventId: 1),
            new LogBufferingFilterRule(logLevel: LogLevel.Information, eventId: 1),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning, eventId: 2),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning, eventId: 1),
            new LogBufferingFilterRule("Program1.MyLogger", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.*MyLogger1", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes: [new("region2", "westus2")]), // inapplicable key
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes:[new("region", "westus3")]), // inapplicable value
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes:[new("region", "westus2")]), // the best rule - [11]
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 2),
            new LogBufferingFilterRule("Program.MyLogger", eventId: 1),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning, eventId: 1),
            new LogBufferingFilterRule("Program", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Error, 1),
        };

        // Act
        LogBufferingFilterRule[] categorySpecificRules = LogBufferingFilterRuleSelector.SelectByCategory(rules, "Program.MyLogger");
        LogBufferingFilterRule? result = LogBufferingFilterRuleSelector.Select(
            categorySpecificRules,
            LogLevel.Warning,
            1,
            [new("region", "westus2")]);

        // Assert
        Assert.Same(rules[11], result);
    }

    [Fact]
    public void WhenManyRuleApply_SelectsLast()
    {
        // Arrange
        var rules = new List<LogBufferingFilterRule>
        {
            new LogBufferingFilterRule(logLevel: LogLevel.Information, eventId: 1),
            new LogBufferingFilterRule(logLevel: LogLevel.Information, eventId: 1),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning, eventId: 2),
            new LogBufferingFilterRule(logLevel: LogLevel.Warning, eventId: 1),
            new LogBufferingFilterRule("Program1.MyLogger", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.*MyLogger1", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.MyLogger*", LogLevel.Warning, 1),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes:[new("region", "westus2")]), // the best rule
            new LogBufferingFilterRule("Program.MyLogger*", LogLevel.Warning, 1, attributes:[new("region", "westus2")]), // same as the best, but last and should be selected
        };

        // Act
        LogBufferingFilterRule[] categorySpecificRules = LogBufferingFilterRuleSelector.SelectByCategory(rules, "Program.MyLogger");
        LogBufferingFilterRule? result = LogBufferingFilterRuleSelector.Select(categorySpecificRules, LogLevel.Warning, 1, [new("region", "westus2")]);

        // Assert
        Assert.Same(rules.Last(), result);
    }

    [Fact]
    public void CanWorkWithValueTypeAttributes()
    {
        // Arrange
        var rules = new List<LogBufferingFilterRule>
        {
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes:[new("priority", 1)]),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes:[new("priority", 2)]), // the best rule
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1, attributes:[new("priority", 3)]),
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning, 1),
        };

        // Act
        LogBufferingFilterRule[] categorySpecificRules = LogBufferingFilterRuleSelector.SelectByCategory(rules, "Program.MyLogger");
        LogBufferingFilterRule? result = LogBufferingFilterRuleSelector.Select(categorySpecificRules, LogLevel.Warning, 1, [new("priority", "2")]);

        // Assert
        Assert.Same(rules[1], result);
    }

    [Fact]
    public void SelectsRightRule_WhenCategoriesHaveDifferentRules()
    {
        // Arrange
        var rules = new List<LogBufferingFilterRule>
        {
            new LogBufferingFilterRule("Program.MyLogger", LogLevel.Warning),
        };
        LogBufferingFilterRule[] myLoggerRules = LogBufferingFilterRuleSelector.SelectByCategory(rules, "Program.MyLogger");
        LogBufferingFilterRule[] otherLoggerRules = LogBufferingFilterRuleSelector.SelectByCategory(rules, "Program.OtherLogger");

        // Act
        LogBufferingFilterRule? myLoggerResult = LogBufferingFilterRuleSelector.Select(myLoggerRules, LogLevel.Warning, 1, attributes: null);
        LogBufferingFilterRule? otherLoggerResult = LogBufferingFilterRuleSelector.Select(otherLoggerRules, LogLevel.Warning, 1, attributes: null);

        // Assert
        Assert.Same(rules[0], myLoggerResult);
        Assert.Null(otherLoggerResult);
    }
}
#endif
