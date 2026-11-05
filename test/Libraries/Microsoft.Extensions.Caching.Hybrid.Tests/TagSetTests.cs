// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Caching.Hybrid.Internal;

namespace Microsoft.Extensions.Caching.Hybrid.Tests;

public class TagSetTests
{
    [Fact]
    public void DefaultEmpty()
    {
        var tags = TagSet.Empty;
        Assert.Equal(0, tags.Count);
        Assert.True(tags.IsEmpty);
        Assert.False(tags.IsArray);
        Assert.Equal("(no tags)", tags.ToString());
        tags.CopyTo(default);
    }

    [Fact]
    public void EmptyArray()
    {
        var tags = TagSet.Create([]);
        Assert.Equal(0, tags.Count);
        Assert.True(tags.IsEmpty);
        Assert.False(tags.IsArray);
        Assert.Equal("(no tags)", tags.ToString());
        tags.CopyTo(default);
    }

    [Fact]
    public void EmptyCustom()
    {
        var tags = TagSet.Create(Custom());
        Assert.Equal(0, tags.Count);
        Assert.True(tags.IsEmpty);
        Assert.False(tags.IsArray);
        Assert.Equal("(no tags)", tags.ToString());
        tags.CopyTo(default);

        static IEnumerable<string> Custom()
        {
            yield break;
        }
    }

    [Fact]
    public void SingleFromArray()
    {
        string[] arr = ["abc"];
        var tags = TagSet.Create(arr);
        arr.AsSpan().Clear(); // to check defensive copy
        Assert.Equal(1, tags.Count);
        Assert.False(tags.IsEmpty);
        Assert.False(tags.IsArray);
        Assert.Equal("abc", tags.ToString());
        var scratch = tags.ToArray();
        Assert.Equal("abc", scratch[0]);
    }

    [Fact]
    public void SingleFromCustom()
    {
        var tags = TagSet.Create(Custom());
        Assert.Equal(1, tags.Count);
        Assert.False(tags.IsEmpty);
        Assert.False(tags.IsArray);
        Assert.Equal("abc", tags.ToString());
        var scratch = tags.ToArray();
        Assert.Equal("abc", scratch[0]);

        static IEnumerable<string> Custom()
        {
            yield return "abc";
        }
    }

    [Fact]
    public void MultipleFromArray()
    {
        string[] arr = ["abc", "def", "ghi"];
        var tags = TagSet.Create(arr);
        arr.AsSpan().Clear(); // to check defensive copy
        Assert.Equal(3, tags.Count);
        Assert.False(tags.IsEmpty);
        Assert.True(tags.IsArray);
        Assert.Equal("abc, def, ghi", tags.ToString());
        var scratch = tags.ToArray();
        Assert.Equal("abc", scratch[0]);
        Assert.Equal("def", scratch[1]);
        Assert.Equal("ghi", scratch[2]);
    }

    [Fact]
    public void MultipleFromCustom()
    {
        var tags = TagSet.Create(Custom());
        Assert.Equal(3, tags.Count);
        Assert.False(tags.IsEmpty);
        Assert.True(tags.IsArray);
        Assert.Equal("abc, def, ghi", tags.ToString());
        var scratch = tags.ToArray();
        Assert.Equal("abc", scratch[0]);
        Assert.Equal("def", scratch[1]);
        Assert.Equal("ghi", scratch[2]);

        static IEnumerable<string> Custom()
        {
            yield return "abc";
            yield return "def";
            yield return "ghi";
        }
    }

    [Fact]
    public void ManyFromArray()
    {
        string[] arr = LongCustom().ToArray();
        var tags = TagSet.Create(arr);
        arr.AsSpan().Clear(); // to check defensive copy
        Assert.Equal(128, tags.Count);
        Assert.False(tags.IsEmpty);
        Assert.True(tags.IsArray);
        var scratch = tags.ToArray();
        Assert.Equal(128, scratch.Length);
    }

    [Fact]
    public void ManyFromCustom()
    {
        var tags = TagSet.Create(LongCustom());
        Assert.Equal(128, tags.Count);
        Assert.False(tags.IsEmpty);
        Assert.True(tags.IsArray);
        var scratch = tags.ToArray();
        Assert.Equal(128, scratch.Length);
    }

    [Fact]
    public void InvalidEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() => TagSet.Create(["abc", "", "ghi"]));
        Assert.Equal("tags", ex.ParamName);
        Assert.StartsWith("Tags cannot be empty.", ex.Message);
    }

    [Fact]
    public void InvalidReserved()
    {
        var ex = Assert.Throws<ArgumentException>(() => TagSet.Create(["abc", "*", "ghi"]));
        Assert.Equal("tags", ex.ParamName);
        Assert.StartsWith("The tag '*' is reserved and cannot be used in this context.", ex.Message);
    }

    private static IEnumerable<string> LongCustom()
    {
        var rand = new Random();
        for (int i = 0; i < 128; i++)
        {
            yield return Create();
        }

        string Create()
        {
            const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            var len = rand.Next(3, 8);
#if NETFRAMEWORK
            char[] chars = new char[len];
#else
            Span<char> chars = stackalloc char[len];
#endif
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[rand.Next(0, Alphabet.Length)];
            }

            return new string(chars);
        }
    }
}
