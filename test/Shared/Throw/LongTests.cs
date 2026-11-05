// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Shared.Diagnostics.Test;

#pragma warning disable S3236 // Caller information arguments should not be provided explicitly

public class LongTests
{
    #region For Long

    [Fact]
    public void IfLongLessThan_ThrowWhenLessThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThan(0L, 1L, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfLongLessThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfLessThan(0L, 0L, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfLongGreaterThan_ThrowWhenGreaterThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThan(1L, 0L, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater than maximum value 0", exception.Message);
    }

    [Fact]
    public void IfLongGreaterThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThan(0L, 0L, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfLongLessThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThanOrEqual(1L, 1L, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less or equal than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfLongLessThanOrEqual_DoesntThrow_WhenGreaterThan()
    {
        var exception = Record.Exception(() => Throw.IfLessThanOrEqual(1L, 0L, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfLongGreaterThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThanOrEqual(1L, 1L, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater or equal than maximum value 1", exception.Message);
    }

    [Fact]
    public void IfLongGreaterThanOrEqual_DoesntThrow_WhenLessThan()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThanOrEqual(0L, 1L, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfLongZero_ThrowWhenZero()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfZero(0L, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is zero", exception.Message);
    }

    [Fact]
    public void IfLongZero_DoesntThrow_WhenNotZero()
    {
        var exception = Record.Exception(() => Throw.IfZero(1L, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void Long_OUtOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(-1L, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(2L, 0, 1, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        Assert.Equal(0, Throw.IfOutOfRange(0L, 0, 1, "foo"));
        Assert.Equal(1, Throw.IfOutOfRange(1L, 0, 1, "foo"));
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThan_For_Long_Get_Correct_Argument_Name()
    {
        const long Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(Zero, -1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(Zero, -1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThanOrEqual_For_Long_Get_Correct_Argument_Name()
    {
        const long Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(Zero, -1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(Zero, -1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThan_For_Long_Get_Correct_Argument_Name()
    {
        const long Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThanOrEqual_For_Long_Get_Correct_Argument_Name()
    {
        const long Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Zero_For_Long_Get_Correct_Argument_Name()
    {
        const long Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_OutOfRange_For_Long_Get_Correct_Argument_Name()
    {
        const long Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1, 2));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1, 2, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion

    #region For Unsigned Long

    [Fact]
    public void IfULongLessThan_ThrowWhenLessThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThan(0UL, 1UL, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfULongLessThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfLessThan(0UL, 0UL, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfULongGreaterThan_ThrowWhenGreaterThan()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThan(1UL, 0UL, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater than maximum value 0", exception.Message);
    }

    [Fact]
    public void IfULongGreaterThan_DoesntThrow_WhenEqual()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThan(0UL, 0UL, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfULongLessThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfLessThanOrEqual(1UL, 1UL, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument less or equal than minimum value 1", exception.Message);
    }

    [Fact]
    public void IfULongLessThanOrEqual_DoesntThrow_WhenGreaterThan()
    {
        var exception = Record.Exception(() => Throw.IfLessThanOrEqual(1UL, 0UL, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfULongGreaterThanOrEqual_ThrowWhenEqual()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfGreaterThanOrEqual(1UL, 1UL, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument greater or equal than maximum value 1", exception.Message);
    }

    [Fact]
    public void IfULongGreaterThanOrEqual_DoesntThrow_WhenLessThan()
    {
        var exception = Record.Exception(() => Throw.IfGreaterThanOrEqual(0UL, 1UL, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void IfULongZero_ThrowWhenZero()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfZero(0UL, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is zero", exception.Message);
    }

    [Fact]
    public void IfULongZero_DoesntThrow_WhenNotZero()
    {
        var exception = Record.Exception(() => Throw.IfZero(1UL, "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void ULong_OutOfRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(0UL, 1UL, 2UL, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange(2L, 0UL, 1UL, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Argument not in the range", exception.Message);

        Assert.Equal(0UL, Throw.IfOutOfRange(0UL, 0, 1, "foo"));
        Assert.Equal(1UL, Throw.IfOutOfRange(1UL, 0, 1, "foo"));
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThan_For_ULong_Get_Correct_Argument_Name()
    {
        const ulong One = 1;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(One, 0UL));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThan(One, 0UL, nameof(One)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_GreaterThanOrEqual_For_ULong_Get_Correct_Argument_Name()
    {
        const ulong One = 1;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(One, 0UL));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfGreaterThanOrEqual(One, 0UL, nameof(One)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThan_For_ULong_Get_Correct_Argument_Name()
    {
        const ulong Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1UL));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThan(Zero, 1UL, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_LessThanOrEqual_For_ULong_Get_Correct_Argument_Name()
    {
        const ulong Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1UL));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfLessThanOrEqual(Zero, 1UL, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Zero_For_ULong_Get_Correct_Argument_Name()
    {
        const ulong Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfZero(Zero, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_OutOfRange_For_ULong_Get_Correct_Argument_Name()
    {
        const ulong Zero = 0;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1UL, 2UL));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange(Zero, 1UL, 2UL, nameof(Zero)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion
}
