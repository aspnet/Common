// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

extern alias Reporting;

using System;
using System.Globalization;
using Xunit;
using IterationNameComparer = Reporting::Microsoft.Extensions.AI.Evaluation.Reporting.Utilities.IterationNameComparer;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

public class IterationNameComparerTests
{
    [Theory]
    [InlineData("1", "2", -1)]
    [InlineData("2", "10", -1)]
    [InlineData("10", "2", 1)]
    [InlineData("3", "3", 0)]
    [InlineData("1.5", "2", -1)]
    [InlineData("1.5", "1.10", 1)]
    [InlineData("Iteration 1", "Iteration 2", -1)]
    [InlineData("1", "Iteration 1", -1)]
    [InlineData("01", "1", -1)]
    [InlineData("1", "01", 1)]
    public void ComparesIterationNames(string first, string second, int expected)
    {
        Assert.Equal(expected, Math.Sign(IterationNameComparer.Default.Compare(first, second)));
    }

    [Fact]
    public void ComparesDecimalIterationNamesIndependentlyOfCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(IterationNameComparer.Default.Compare("1.5", "2") < 0);
            Assert.True(IterationNameComparer.Default.Compare("1.10", "1.5") < 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
