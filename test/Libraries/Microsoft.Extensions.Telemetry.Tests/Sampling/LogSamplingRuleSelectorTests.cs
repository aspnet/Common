// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Diagnostics.Sampling;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Telemetry.Sampling;

public class LogSamplingRuleSelectorTests
{
    private readonly LogSamplingRuleSelector<RandomProbabilisticSamplerFilterRule> _ruleSelector = new();

    [Fact]
    public void SelectsRightRule()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule (probability: 0),
            new RandomProbabilisticSamplerFilterRule (probability: 0, eventId: 1),
            new RandomProbabilisticSamplerFilterRule (probability: 0, logLevel: LogLevel.Information, eventId: 1 ),
            new RandomProbabilisticSamplerFilterRule (probability: 0, logLevel: LogLevel.Information, eventId: 1 ),
            new RandomProbabilisticSamplerFilterRule (probability: 0, logLevel: LogLevel.Warning),
            new RandomProbabilisticSamplerFilterRule (probability: 0, logLevel : LogLevel.Warning, eventId : 2),
            new RandomProbabilisticSamplerFilterRule (probability: 0, logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule (probability: 0, categoryName: "Program1.MyLogger", logLevel: LogLevel.Warning, eventId: 1),
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program.*MyLogger1", logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program.MyLogger", logLevel : LogLevel.Warning, eventId : 1), // the best rule
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program.MyLogger", logLevel : LogLevel.Warning, eventId : 2),
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program.MyLogger", eventId : 1),
            new RandomProbabilisticSamplerFilterRule (probability : 0, logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program", logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program.MyLogger", logLevel : LogLevel.Warning),
            new RandomProbabilisticSamplerFilterRule (probability : 0, categoryName : "Program.MyLogger", logLevel : LogLevel.Error, eventId : 1),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "Program.MyLogger", LogLevel.Warning, 1);

        // Assert
        Assert.Same(rules[9], actualResult);
    }

    [Fact]
    public void WhenManyRuleApply_SelectsLast()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability : 0, logLevel : LogLevel.Information, eventId : 1),
            new RandomProbabilisticSamplerFilterRule(probability : 0, logLevel : LogLevel.Information, eventId : 1),
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Warning),
            new RandomProbabilisticSamplerFilterRule(probability : 0, logLevel : LogLevel.Warning, eventId : 2),
            new RandomProbabilisticSamplerFilterRule(probability : 0, logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule(probability : 0, categoryName : "Program1.MyLogger", logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule(probability : 0, categoryName : "Program.*MyLogger1", logLevel : LogLevel.Warning, eventId : 1),
            new RandomProbabilisticSamplerFilterRule(probability : 0, categoryName : "Program.MyLogger", logLevel : LogLevel.Warning, eventId : 1), // the best rule
            new RandomProbabilisticSamplerFilterRule(probability: 0, categoryName: "Program.MyLogger*", logLevel: LogLevel.Warning, eventId: 1), // same as the best, but last, and should be selected
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "Program.MyLogger", LogLevel.Warning, 1);

        // Assert
        Assert.Same(rules.Last(), actualResult);
    }

    [Fact]
    public void SelectsRuleBasedOnLogLevel()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Information),
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Warning), // the best rule
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Error),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "AnyCategory", LogLevel.Warning, 0);

        // Assert
        Assert.Same(rules[1], actualResult);
    }

    [Fact]
    public void SelectsRuleBasedOnEventId()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability: 0, eventId: 1),
            new RandomProbabilisticSamplerFilterRule(probability: 0, eventId: 2), // the best rule
            new RandomProbabilisticSamplerFilterRule(probability: 0, eventId: 3),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "AnyCategory", LogLevel.Information, 2);

        // Assert
        Assert.Same(rules[1], actualResult);
    }

    [Fact]
    public void LogLevelSpecificityRemainsAheadOfEventIdSpecificity()
    {
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new(probability: 0.1, logLevel: LogLevel.Information),
            new(probability: 1.0, eventId: 42),
        };

        RandomProbabilisticSamplerFilterRule? actualResult =
            _ruleSelector.Select(rules, "AnyCategory", LogLevel.Information, 42);

        Assert.Same(rules[0], actualResult);
    }

    [Fact]
    public void SelectsRuleBasedOnLogLevelAndEventId()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Information, eventId: 1),
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Warning, eventId: 2), // the best rule
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Error, eventId: 3),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "AnyCategory", LogLevel.Warning, 2);

        // Assert
        Assert.Same(rules[1], actualResult);
    }

    [Fact]
    public void SelectsRuleWithWildcardCategory()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability: 0, categoryName: "Program.*"),
            new RandomProbabilisticSamplerFilterRule(probability: 0, categoryName: "Program.MyLogger*"), // the best rule
            new RandomProbabilisticSamplerFilterRule(probability: 0, categoryName: "Program.MyLogger"),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "Program.MyLogger", LogLevel.Information, 0);

        // Assert
        Assert.Same(rules[1], actualResult);
    }

    [Fact]
    public void SelectsRuleWithoutCategory()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability: 0),
            new RandomProbabilisticSamplerFilterRule(probability: 0, categoryName: "Program.MyLogger"),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "AnyCategory", LogLevel.Information, 0);

        // Assert
        Assert.Same(rules[0], actualResult);
    }

    [Fact]
    public void SelectsRuleWithHigherLogLevelWhenNoExactMatch()
    {
        // Arrange
        var rules = new List<RandomProbabilisticSamplerFilterRule>
        {
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Error), // the best rule
            new RandomProbabilisticSamplerFilterRule(probability: 0, logLevel: LogLevel.Critical),
        };

        // Act
        RandomProbabilisticSamplerFilterRule? actualResult = _ruleSelector.Select(rules, "AnyCategory", LogLevel.Warning, 0);

        // Assert
        Assert.Same(rules[0], actualResult);
    }
}
