// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI.Evaluation.NLP.Common;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.NLP.Tests;

public class SimpleTokenizerTests
{
    [Theory]
    [InlineData(" $41.23 ", new[] { "$", "41.23" })]
    [InlineData("word", new[] { "WORD" })]
    [InlineData("word1 word2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1,word2", new[] { "WORD1", ",", "WORD2" })]
    [InlineData("word1.word2", new[] { "WORD1", ".", "WORD2" })]
    [InlineData("word1!word2?", new[] { "WORD1", "!", "WORD2", "?" })]
    [InlineData("word1-word2", new[] { "WORD1", "-", "WORD2" })]
    [InlineData("word1 - word2", new[] { "WORD1", "-", "WORD2" })]
    [InlineData("word1-\n word2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1-", new[] { "WORD1", "-" })]
    [InlineData("word1&", new[] { "WORD1", "&" })]
    [InlineData("word1-\r\n word2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1-\r\nword2", new[] { "WORD1WORD2" })]
    [InlineData("word1-\nword2", new[] { "WORD1WORD2" })]
    [InlineData("word1\nword2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1 \n word2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1\r\nword2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1 \r\n word2", new[] { "WORD1", "WORD2" })]
    [InlineData("word1\tword2", new[] { "WORD1", "WORD2" })]
    [InlineData("It is a guide to action that ensures that the military will forever heed Party commands.",
        new[] { "IT", "IS", "A", "GUIDE", "TO", "ACTION", "THAT", "ENSURES", "THAT", "THE", "MILITARY", "WILL", "FOREVER", "HEED", "PARTY", "COMMANDS", "." })]
    [InlineData("Good muffins cost $3.88 (roughly 3,36 euros)\nin New York.  Please buy me\ntwo of them.\nThanks.",
        new[] { "GOOD", "MUFFINS", "COST", "$", "3.88", "(", "ROUGHLY", "3,36", "EUROS", ")", "IN", "NEW", "YORK", ".", "PLEASE", "BUY", "ME", "TWO", "OF", "THEM", ".", "THANKS", "." })]
    [InlineData("I have 42.", new[] { "I", "HAVE", "42", "." })]
    [InlineData("It costs 3,36.", new[] { "IT", "COSTS", "3,36", "." })]
    [InlineData("42, 43", new[] { "42", ",", "43" })]
    [InlineData("42%", new[] { "42", "%" })]
    [InlineData("(42)", new[] { "(", "42", ")" })]
    [InlineData("42.x", new[] { "42", ".", "X" })]
    [InlineData("1.2.3", new[] { "1.2.3" })]
    [InlineData("", new string[0])]
    [InlineData(" This is a test.", new[] { "THIS", "IS", "A", "TEST", "." })]
    [InlineData("Hello, world! How's it going?", new[] { "HELLO", ",", "WORLD", "!", "HOW", "'", "S", "IT", "GOING", "?" })]
    [InlineData("&quot;Quotes&quot; and &amp; symbols &lt; &gt; &apos;", new[] { "\"", "QUOTES", "\"", "AND", "&", "SYMBOLS", "<", ">", "'" })]
    [InlineData("-\nThis is a test.", new[] { "THIS", "IS", "A", "TEST", "." })]
    public void Tokenize_Cases(string input, string[] expected)
    {
        var result = SimpleWordTokenizer.WordTokenize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HandlesMultipleSpacesAndEmptyEntries()
    {
        var input = "   word1   word2    word3   ";
        var expected = new[] { "WORD1", "WORD2", "WORD3" };
        var result = SimpleWordTokenizer.WordTokenize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HandlesUnicodeSymbolsAndPunctuation()
    {
        var input = "word1 © word2 ™ word3 — word4";
        var expected = new[] { "WORD1", "©", "WORD2", "™", "WORD3", "—", "WORD4" };
        var result = SimpleWordTokenizer.WordTokenize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HandlesHtmlEntities()
    {
        var input = "&quot;Hello&quot; &amp; Goodbye &lt;test&gt; &apos;";
        var expected = new[] { "\"", "HELLO", "\"", "&", "GOODBYE", "<", "TEST", ">", "'" };
        var result = SimpleWordTokenizer.WordTokenize(input);
        Assert.Equal(expected, result);
    }
}
