// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Shared.Diagnostics.Test;

#pragma warning disable S3236 // Caller information arguments should not be provided explicitly

public class IntegerTests
{
    #region For Integer

    [Fact]
    public void IfIntLessThan_ThrowWhenLessThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThan(0, 1, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfIntLessThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfLessThan(0, 0, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfIntGreaterThan_ThrowWhenGreaterThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThan(1, 0, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater than maximum value 0", exception.Message);
    }

    [Fact]
    public void IfIntGreaterThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThan(0, 0, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfIntLessThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThanOrEqual(1, 1, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less or equal than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfIntLessThanOrEqual_DoesntThrow_WhenGreaterThan()
    {
        var exception = Record.Exception(() => Throw.IfLessThanOrEqual(1, 0, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfIntGreaterThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThanOrEqual(1, 1, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater or equal than maximum value 1", exception.Message);
    }

    [Fact]
    public void IfIntGreaterThanOrEqual_DoesntThrow_WhenLessThan()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThanOrEqual(0, 1, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfIntZero_ThrowWhenZero()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfZero(0, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is zero", exception.Message);
    }

    [Fact]
    public void IfIntZero_DoesntThrow_WhenNotZero()
    {
        var exception = Record.Exception(() => Throw.IfZero(1, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void Int_OUtOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(-1, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(2, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        Assert.Equal(0, Throw.IfOutOfRange(0, 0, 1, "foo"));
        Assert.Equal(1, Throw.IfOutOfRange(1, 0, 1, "foo"));
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThan_For_Int_Get_Correct_Argument_Name()
    {
        const int Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(Zero, -1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(Zero, -1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThanOrEqual_For_Int_Get_Correct_Argument_Name()
    {
        const int Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(Zero, -1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(Zero, -1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThan_For_Int_Get_Correct_Argument_Name()
    {
        const int Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThanOrEqual_For_Int_Get_Correct_Argument_Name()
    {
        const int Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Zero_For_Int_Get_Correct_Argument_Name()
    {
        const int Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_OutOfRange_For_Int_Get_Correct_Argument_Name()
    {
        const int Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1, 2));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1, 2, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion

    #region For Unsigned Integer

    [Fact]
    public void IfUIntLessThan_ThrowWhenLessThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThan(0U, 1U, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfUIntLessThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfLessThan(0U, 0U, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfUIntGreaterThan_ThrowWhenGreaterThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThan(1U, 0U, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater than maximum value 0", exception.Message);
    }

    [Fact]
    public void IfUIntGreaterThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThan(0U, 0U, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfUIntLessThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThanOrEqual(1U, 1U, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less or equal than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfUIntLessThanOrEqual_DoesntThrow_WhenGreaterThan()
    {
        var exception = Record.Exception(() => Throw.IfLessThanOrEqual(1U, 0U, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfUIntGreaterThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThanOrEqual(1U, 1U, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater or equal than maximum value 1", exception.Message);
    }

    [Fact]
    public void IfUIntGreaterThanOrEqual_DoesntThrow_WhenLessThan()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThanOrEqual(0U, 1U, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfUIntZero_ThrowWhenZero()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfZero(0U, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is zero", exception.Message);
    }

    [Fact]
    public void IfUIntZero_DoesntThrow_WhenNotZero()
    {
        var exception = Record.Exception(() => Throw.IfZero(1U, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void UInt_OutOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(0U, 1, 2, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(2U, 0U, 1U, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        Assert.Equal(0U, Throw.IfOutOfRange(0U, 0, 1, "foo"));
        Assert.Equal(1U, Throw.IfOutOfRange(1U, 0, 1, "foo"));
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThan_For_UInt_Get_Correct_Argument_Name()
    {
        const uint One = 1;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(One, 0U));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(One, 0U, nameof(One)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThanOrEqual_For_UInt_Get_Correct_Argument_Name()
    {
        const uint One = 1;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(One, 0U));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(One, 0U, nameof(One)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThan_For_UInt_Get_Correct_Argument_Name()
    {
        const uint Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1U));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1U, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThanOrEqual_For_UInt_Get_Correct_Argument_Name()
    {
        const uint Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1U));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1U, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Zero_For_UInt_Get_Correct_Argument_Name()
    {
        const uint Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_OutOfRange_For_UInt_Get_Correct_Argument_Name()
    {
        const uint Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1U, 2U));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1U, 2U, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion
}
