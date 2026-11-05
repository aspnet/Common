// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.Logging.Test;

public static class LoggerMessageHelperTests
{
    [Theory]
    [InlineData(null, "null")]
    [InlineData(new[] { "One" }, "[\"One\"]")]
    [InlineData(new[] { "One", "Two" }, "[\"One\",\"Two\"]")]
    [InlineData(new[] { "One", null }, "[\"One\",null]")]
    [InlineData(new[] { 1, 2, 3 }, "[\"1\",\"2\",\"3\"]")]
    public static void Enumerate(IEnumerable? enumerable, string expected)
    {
        Assert.Equal(expected, LoggerMessageHelper.Stringify(enumerable));
    }

    [Fact]
    public static void EnumerateKeyValuePair()
    {
        Assert.Equal("null", LoggerMessageHelper.Stringify((IEnumerable<KeyValuePair<string, string>>?)null));

        var d0 = new Dictionary<string, string>
        {
            { "One", "Un" }
        };
        Assert.Equal("{\"One\"=\"Un\"}", LoggerMessageHelper.Stringify(d0));

        var d1 = new Dictionary<string, string>
        {
            { "One", "Un" },
            { "Two", "Deux" }
        };
        Assert.Equal("{\"One\"=\"Un\",\"Two\"=\"Deux\"}", LoggerMessageHelper.Stringify(d1));

        var d2 = new List<KeyValuePair<string?, string?>>
        {
            new(null, "Un"),
            new("Two", null),
        };
        Assert.Equal("{null=\"Un\",\"Two\"=null}", LoggerMessageHelper.Stringify(d2));

        var d3 = new Dictionary<string, int>
        {
            { "Zero", 0 },
            { "One", 1 },
            { "Two", 2 }
        };
        Assert.Equal("{\"Zero\"=\"0\",\"One\"=\"1\",\"Two\"=\"2\"}", LoggerMessageHelper.Stringify(d3));

        var d4 = new Dictionary<int, string>
        {
            { 0, "Zero" },
            { 1, "One" },
            { 2, "Two" }
        };
        Assert.Equal("{\"0\"=\"Zero\",\"1\"=\"One\",\"2\"=\"Two\"}", LoggerMessageHelper.Stringify(d4));
    }

    [Fact]
    public static void ThreadLocal()
    {
        var lmp1 = LoggerMessageHelper.ThreadLocalState;
        Assert.NotNull(lmp1);

        var lmp2 = LoggerMessageHelper.ThreadLocalState;
        Assert.Same(lmp1, lmp2);
    }

    [Theory]
    [InlineData(1, 0, "")]
    [InlineData(0, 1, "")]
    [InlineData(0, 0, "prefix")]
    public static void ThreadLocal_IsNotSharedWhileInUse(int tags, int classifiedTags, string tagNamePrefix)
    {
        var outer = LoggerMessageHelper.ThreadLocalState;
        _ = outer.ReserveTagSpace(tags);
        _ = outer.ReserveClassifiedTagSpace(classifiedTags);
        outer.TagNamePrefix = tagNamePrefix;

        // Like a nested logging call on the same thread, e.g. from a logger provider.
        var nested = LoggerMessageHelper.ThreadLocalState;
        Assert.NotSame(outer, nested);
        Assert.Equal(0, nested.TagsCount);
        Assert.Equal(0, nested.ClassifiedTagsCount);
        Assert.Equal(string.Empty, nested.TagNamePrefix);

        nested.AddTag("nested", "value");
        nested.Clear();

        Assert.Equal(tags, outer.TagsCount);
        Assert.Equal(classifiedTags, outer.ClassifiedTagsCount);
        Assert.Equal(tagNamePrefix, outer.TagNamePrefix);
        outer.Clear();

        Assert.Same(nested, LoggerMessageHelper.ThreadLocalState);
        Assert.Same(nested, LoggerMessageHelper.ThreadLocalState);
    }
}
