// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Extensions.Http.Logging.Test;

public class HttpStatusCodeLogLevelRuleTest
{
    [Fact]
    public void Validate_ValidRange_ReturnsNoErrors()
    {
        var rule = new HttpStatusCodeLogLevelRule
        {
            FromStatusCode = 400,
            ToStatusCode = 499,
            LogLevel = LogLevel.Warning,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(rule, new ValidationContext(rule), results, true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_ToLessThanFrom_ReturnsError()
    {
        var rule = new HttpStatusCodeLogLevelRule
        {
            FromStatusCode = 500,
            ToStatusCode = 400,
            LogLevel = LogLevel.Warning,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(rule, new ValidationContext(rule), results, true);

        Assert.False(isValid);
        Assert.Single(results);
        Assert.Contains(nameof(HttpStatusCodeLogLevelRule.ToStatusCode), results[0].MemberNames);
    }

    [Fact]
    public void Validate_NullToStatusCode_IsValid()
    {
        var rule = new HttpStatusCodeLogLevelRule
        {
            FromStatusCode = 404,
            ToStatusCode = null,
            LogLevel = LogLevel.Debug,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(rule, new ValidationContext(rule), results, true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_FromStatusCodeOutOfRange_ReturnsError()
    {
        var rule = new HttpStatusCodeLogLevelRule
        {
            FromStatusCode = 99,
            LogLevel = LogLevel.Warning,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(rule, new ValidationContext(rule), results, true);

        Assert.False(isValid);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void Validate_EqualFromAndTo_IsValid()
    {
        var rule = new HttpStatusCodeLogLevelRule
        {
            FromStatusCode = 404,
            ToStatusCode = 404,
            LogLevel = LogLevel.Warning,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(rule, new ValidationContext(rule), results, true);

        Assert.True(isValid);
        Assert.Empty(results);
    }
}
