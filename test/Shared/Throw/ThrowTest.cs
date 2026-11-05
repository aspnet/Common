// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Shared.Diagnostics.Test;

#pragma warning disable S3236 // Caller information arguments should not be provided explicitly

public class ThrowTest
{
    #region Exceptions

    [Fact]
    public void ThrowInvalidOperationException_ThrowsException_WithMessage()
    {
        var message = "message";
        var exception = Assert.Throws<InvalidOperationException>(() => Throw.InvalidOperationException(message));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowInvalidOperationException_ThrowsException_WithMessageAndInnerException()
    {
        var message = "message";
#pragma warning disable CA2201 // Do not raise reserved exception types
        var innerException = new Exception();
#pragma warning restore CA2201 // Do not raise reserved exception types
        var exception = Assert.Throws<InvalidOperationException>(() => Throw.InvalidOperationException(message, innerException));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void ThrowArgumentException_ThrowsException_WithMessageAndParamName()
    {
        var message = "message";
        var paramName = "paramName";
        var exception = Assert.Throws<ArgumentException>(() => Throw.ArgumentException(paramName, message));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowArgumentException_ThrowsException_WithMessageAndParamNameAndInnerException()
    {
        var message = "message";
        var paramName = "paramName";
#pragma warning disable CA2201 // Do not raise reserved exception types
        var innerException = new Exception();
#pragma warning restore CA2201 // Do not raise reserved exception types
        var exception = Assert.Throws<ArgumentException>(() => Throw.ArgumentException(paramName, message, innerException));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(paramName, exception.ParamName);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void ThrowArgumentNullException_ThrowsException_WithMessage()
    {
        var paramName = "paramName";
        var exception = Assert.Throws<ArgumentNullException>(() => Throw.ArgumentNullException(paramName));
        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowArgumentNullException_ThrowsException_WithMessageAndParamName()
    {
        var paramName = "paramName";
        var message = "message";
        var exception = Assert.Throws<ArgumentNullException>(() => Throw.ArgumentNullException(paramName, message));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowArgumentOutOfRangeException_ThrowsException_WithParamName()
    {
        var paramName = "paramName";
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.ArgumentOutOfRangeException(paramName));
        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowArgumentOutOfRangeException_ThrowsException_WithMessageAndParamName()
    {
        var paramName = "paramName";
        var message = "message";
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.ArgumentOutOfRangeException(paramName, message));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowArgumentOutOfRangeException_ThrowsException_WithMessageAndActualValue()
    {
        var paramName = "paramName";
        var message = "message";
        var actualValue = 10;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.ArgumentOutOfRangeException(paramName, actualValue, message));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(paramName, exception.ParamName);
        Assert.Equal(actualValue, exception.ActualValue);
    }

    #endregion

    #region For Object

    [Fact]
    public void ThrowIfNull_ThrowsWhenNullValue_ReferenceType()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNull<string>(null!, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
    }

    [Fact]
    public void ThrowIfNull_ThrowsWhenNullValue_NullableValueType()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNull<int?>(null!, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
    }

    [Fact]
    public void ThrowIfNull_DoesntThrow_WhenNotNullReferenceType()
    {
        var value = string.Empty;
        Assert.Equal(value, Throw.IfNull(value, nameof(value)));
    }

    [Fact]
    public void ThrowIfNull_DoesntThrow_WhenNullableValueType()
    {
        int? value = 0;
        Assert.Equal(value, Throw.IfNull(value, nameof(value)));
    }

    [Fact]
    public void Shorter_Version_Of_Throws_Get_Correct_Argument_Name()
    {
        Random? somethingThatIsNull = null;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfNull(somethingThatIsNull));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfNull(somethingThatIsNull, nameof(somethingThatIsNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Throws_Get_Correct_Argument_Name_For_Object_Checks()
    {
        Random? somethingThatIsNull = null;
        Random? somethingNestedThatIsNull = null;
        object somethingThatIsNotNull = new();

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfNullOrMemberNull(somethingThatIsNull, somethingNestedThatIsNull));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(
            () => Throw.IfNullOrMemberNull(somethingThatIsNull, somethingNestedThatIsNull, nameof(somethingThatIsNull), nameof(somethingNestedThatIsNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_Throws_Get_Correct_Argument_Name_For_Member_Checks()
    {
        Color red = Color.Red;
        Random? somethingNestedThatIsNull = null;
        object somethingThatIsNotNull = new();

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfNullOrMemberNull(red, somethingNestedThatIsNull));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(
            () => Throw.IfNullOrMemberNull(red, somethingNestedThatIsNull, nameof(red), nameof(somethingNestedThatIsNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);

        var expectedMessage = $"Member {nameof(somethingNestedThatIsNull)} of {nameof(red)} is null";
#if NETCOREAPP3_1_OR_GREATER
        expectedMessage += $" (Parameter '{nameof(red)}')";
#else
        expectedMessage += $"\r\nParameter name: {nameof(red)}";
#endif
        Assert.Equal(expectedMessage, exceptionImplicitArgumentName.Message);

        exceptionImplicitArgumentName = Record.Exception(() => Throw.IfMemberNull(somethingThatIsNotNull, somethingNestedThatIsNull));
        Assert.NotNull(exceptionImplicitArgumentName);
        exceptionExplicitArgumentName = Record.Exception(
            () => Throw.IfMemberNull(somethingThatIsNotNull, somethingNestedThatIsNull, nameof(somethingThatIsNotNull), nameof(somethingNestedThatIsNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);

        expectedMessage = $"Member {nameof(somethingNestedThatIsNull)} of {nameof(somethingThatIsNotNull)} is null";
#if NETCOREAPP3_1_OR_GREATER
        expectedMessage += $" (Parameter '{nameof(somethingThatIsNotNull)}')";
#else
        expectedMessage += $"\r\nParameter name: {nameof(somethingThatIsNotNull)}";
#endif
        Assert.Equal(expectedMessage, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_IfNull_Does_Not_Throw_When_Member_Is_Not_Null()
    {
        Color red = Color.Red;
        Color blue = Color.Blue;

        var exception = Record.Exception(() => Throw.IfNullOrMemberNull(red, blue));
        Assert.Null(exception);

        var resultOfThrows = Throw.IfNullOrMemberNull(red, blue);
        Assert.Equal(resultOfThrows, blue);

        resultOfThrows = Throw.IfMemberNull(red, blue);
        Assert.Equal(resultOfThrows, blue);
    }

    #endregion

    #region For String
    [Fact]
    public void ThrowIfNullOrWhitespace_ThrowsWhenNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrWhitespace(null!, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
    }

    [Fact]
    public void ThrowIfNullOrWhitespace_ThrowsWhenWhitespace()
    {
        var exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrWhitespace("  ", "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is whitespace", exception.Message);
    }

    [Fact]
    public void ThrowIfNullOrWhitespace_DoesntThrow_WhenNotNullOrWhitespace()
    {
        var exception = Record.Exception(() => Throw.IfNullOrWhitespace("param", "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ThrowsWhenNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrEmpty(null, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ThrowsWhenEmpty()
    {
        var exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrEmpty("", "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Argument is an empty string", exception.Message);
    }

    [Fact]
    public void ThrowIfNullOrWhitespace_DoesntThrow_WhenNotNullOrEmpty()
    {
        var exception = Record.Exception(() => Throw.IfNullOrEmpty("param", "paramName"));
        Assert.Null(exception);
    }

    [Fact]
    public void Shorter_Version_Of_ThrowIfNullOrWhitespace_Get_Correct_Argument_Name()
    {
        string? somethingThatIsNull = null;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfNullOrWhitespace(somethingThatIsNull));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfNullOrWhitespace(somethingThatIsNull, nameof(somethingThatIsNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    [Fact]
    public void Shorter_Version_Of_ThrowIfNullOrEmpty_Get_Correct_Argument_Name()
    {
        string? somethingThatIsNull = null;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfNullOrEmpty(somethingThatIsNull));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfNullOrEmpty(somethingThatIsNull, nameof(somethingThatIsNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion

    #region For Buffer

    [Fact]
    public void IfBufferTooSmall_ThrowsWhenBufferTooSmall()
    {
        var exception = Assert.Throws<ArgumentException>(() => Throw.IfBufferTooSmall(23, 24, "paramName"));
        Assert.Equal("paramName", exception.ParamName);
        Assert.StartsWith("Buffer", exception.Message);
    }

    [Fact]
    public void IfBufferTooSmall_DoesntThrow_WhenBufferIsLargeEnough()
    {
        var exception = Record.Exception(() => Throw.IfBufferTooSmall(23, 23, "paramName"));
        Assert.Null(exception);
    }
    #endregion

    #region For Collections

    private static IEnumerable<int> GetEmptyEnumerable()
    {
        yield break;
    }

    private static IEnumerable<int> GetNonemptyEnumerable()
    {
        yield return 1;
    }

    [Fact]
    public void Collection_IfNullOrEmpty()
    {
        ArgumentException exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrEmpty((ICollection<int>?)null, "foo"));
        Assert.Equal("foo", exception.ParamName);

        exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrEmpty((IReadOnlyCollection<int>?)null, "foo"));
        Assert.Equal("foo", exception.ParamName);

        exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrEmpty((List<int>?)null, "foo"));
        Assert.Equal("foo", exception.ParamName);

        exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrEmpty((Queue<int>?)null, "foo"));
        Assert.Equal("foo", exception.ParamName);

        exception = Assert.Throws<ArgumentNullException>(() => Throw.IfNullOrEmpty((IEnumerable<int>?)null, "foo"));
        Assert.Equal("foo", exception.ParamName);

        var list = new List<int>();

        exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrEmpty((ICollection<int>?)list, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Collection is empty", exception.Message);

        exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrEmpty((IReadOnlyCollection<int>?)list, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Collection is empty", exception.Message);

        exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrEmpty(list, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Collection is empty", exception.Message);

        var queue = new Queue<int>();

        exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrEmpty(queue, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Collection is empty", exception.Message);

        var enumerable = GetEmptyEnumerable();

        exception = Assert.Throws<ArgumentException>(() => Throw.IfNullOrEmpty(enumerable, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.StartsWith("Collection is empty", exception.Message);

        list.Add(42);
        Assert.Equal(list, Throw.IfNullOrEmpty((ICollection<int>?)list, "foo"));
        Assert.Equal(list, Throw.IfNullOrEmpty((IReadOnlyCollection<int>?)list, "foo"));
        Assert.Equal(list, Throw.IfNullOrEmpty(list, "foo"));

        queue.Enqueue(42);
        Assert.Equal(queue, Throw.IfNullOrEmpty(queue, "foo"));

        enumerable = GetNonemptyEnumerable();
        Assert.Equal(enumerable, Throw.IfNullOrEmpty(enumerable, "foo"));
    }

    [Fact]
    public void Shorter_Version_Of_NullOrEmpty_Get_Correct_Argument_Name()
    {
        List<int>? listButActuallyNull = null;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfNullOrEmpty(listButActuallyNull!));

        Assert.NotNull(exceptionImplicitArgumentName);
        Assert.Contains(nameof(listButActuallyNull), exceptionImplicitArgumentName.Message);
    }

    #endregion

    #region For Enums

    internal enum Color
    {
        Red,
        Green,
        Blue,
    }

    [Fact]
    public void Enum_OutOfRange()
    {
        Assert.Equal(Color.Red, Throw.IfOutOfRange(Color.Red, "foo"));
        Assert.Equal(Color.Green, Throw.IfOutOfRange(Color.Green, "foo"));
        Assert.Equal(Color.Blue, Throw.IfOutOfRange(Color.Blue, "foo"));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange((Color)(-1), "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.Contains("is an invalid value for enum type", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => Throw.IfOutOfRange((Color)3, "foo"));
        Assert.Equal("foo", exception.ParamName);
        Assert.Contains("is an invalid value for enum type", exception.Message);
    }

    [Fact]
    public void Shorter_Version_Of_OutOfRange_Get_Correct_Argument_Name()
    {
        Color? colorButNull = null;

        var exceptionImplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange((Color)colorButNull!));
        Assert.NotNull(exceptionImplicitArgumentName);
        var exceptionExplicitArgumentName = Record.Exception(() => Throw.IfOutOfRange((Color)colorButNull!, nameof(colorButNull)));

        Assert.NotNull(exceptionExplicitArgumentName);
        Assert.Equal(exceptionExplicitArgumentName.Message, exceptionImplicitArgumentName.Message);
    }

    #endregion
}
