// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Shared.Diagnostics.Test;

#pragma warning disable S3236 // Caller information arguments should not be provided explicitly

public class DoubleTests
{
    #region For Double

    [Fact]
    public void IfDoubleLessThan_ThrowWhenLessThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThan(0.0, 1.0, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less than minimum value 1", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThan(double.NaN, 1.0, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfDoubleLessThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfLessThan(0.0, 0.0, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfDoubleGreaterThan_ThrowWhenGreaterThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThan(1.4, 0.0, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater than maximum value 0", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThan(double.NaN, 0.0, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater than maximum value 0", exception.Message);
    }

    [Fact]
    public void IfDoubleGreaterThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThan(0.0, 0.0, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfDoubleLessThanOrEqual_ThrowWhenEqual()
    {
        const double TestValue = 1.2;
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThanOrEqual(TestValue, TestValue, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith($"Argument less or equal than minimum value {TestValue}", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThanOrEqual(double.NaN, TestValue, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith($"Argument less or equal than minimum value {TestValue}", exception.Message);
    }

    [Fact]
    public void IfDoubleLessThanOrEqual_DoesntThrow_WhenGreaterThan()
    {
        var exception = Record.Exception(() => Throw.IfLessThanOrEqual(1.5, 0.0, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfDoubleGreaterThanOrEqual_ThrowWhenEqual()
    {
        const double TestValue = 1.22;
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThanOrEqual(TestValue, TestValue, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith($"Argument greater or equal than maximum value {TestValue}", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThanOrEqual(double.NaN, TestValue, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith($"Argument greater or equal than maximum value {TestValue}", exception.Message);
    }

    [Fact]
    public void IfDoubleGreaterThanOrEqual_DoesntThrow_WhenLessThan()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThanOrEqual(0.0, 1.3, "paramName"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-0.0)]
    [InlineData(-0)]
    public void IfDoubleZero_ThrowWhenZero(double zero)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfZero(zero, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is zero", exception.Message);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(-0.010)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void IfDoubleZero_DoesntThrow_WhenNotZero(double notZero)
    {
        var exception = Record.Exception(() => Throw.IfZero(notZero, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void Double_OutOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(-1.0, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(2.0, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        Assert.Equal(0, Throw.IfOutOfRange(0.0, 0, 1, "foo"));
        Assert.Equal(1, Throw.IfOutOfRange(1.0, 0, 1, "foo"));

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(double.NaN, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThan_For_Double_Get_Correct_Argument_Name()
    {
        const double Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(Zero, -1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(Zero, -1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThanOrEqual_For_Double_Get_Correct_Argument_Name()
    {
        const double Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(Zero, -1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(Zero, -1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThan_For_Double_Get_Correct_Argument_Name()
    {
        const double Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThanOrEqual_For_Double_Get_Correct_Argument_Name()
    {
        const double Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Zero_For_Double_Get_Correct_Argument_Name()
    {
        const double Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_OutOfRange_For_Double_Get_Correct_Argument_Name()
    {
        const double Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1, 2));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1, 2, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion
}
