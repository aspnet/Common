// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Extensions.AI.Evaluation.NLP.Common;
using Xunit;
using static Microsoft.Extensions.AI.Evaluation.NLP.Common.GLEUAlgorithm;

namespace Microsoft.Extensions.AI.Evaluation.NLP.Tests;

public class GLEUAlgorithmTests
{
    [Fact]
    public void TestZeroMatches()
    {
        string[][] references = ["The candidate has no alignment to any of the references".Split(' '),];
        string[] hypothesis = "John loves Mary".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.0, score, 4);
    }

    [Fact]
    public void TestFullMatches()
    {
        string[][] references = ["John loves Mary".Split(' '),];
        string[] hypothesis = "John loves Mary".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(1.0, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUUsesTheBestMatchingReference()
    {
        string[][] references = [
            "the cat sat on the mat".Split(' '),
            "completely different text here now".Split(' '),
        ];
        string[] hypothesis = "the cat sat on the mat".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(1.0, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUFindsTheBestReferenceInAnyPosition()
    {
        string[][] references = [
            "completely different text here now".Split(' '),
            "nothing in common with the response".Split(' '),
            "the cat sat on the mat".Split(' '),
        ];
        string[] hypothesis = "the cat sat on the mat".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(1.0, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUIgnoresAdditionalWorseReferences()
    {
        string[][] references = [
            "It is a guide to action that ensures that the military will forever heed Party commands".Split(' '),
            "It is the guiding principle which guarantees the military forces always being under the command of the Party".Split(' '),
        ];
        string[] hypothesis = "It is a guide to action which ensures that the military always obeys the commands of the party".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.43939, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUExampleA()
    {
        string[][] references = [
            "It is a guide to action that ensures that the military will forever heed Party commands".Split(' '),
            "It is the guiding principle which guarantees the military forces always being under the command of the Party".Split(' '),
            "It is the practical guide for the army always to heed the directions of the party".Split(' ')
        ];
        string[] hypothesis = "It is a guide to action which ensures that the military always obeys the commands of the party".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.43939, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUMilitaryExampleA()
    {
        string[][] references = [
            "It is a guide to action that ensures that the military will forever heed Party commands".Split(' '),
        ];
        string[] hypothesis = "It is a guide to action which ensures that the military always obeys the commands of the party".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.43939, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUMilitaryExampleB()
    {
        string[][] references = [
            "It is a guide to action that ensures that the military will forever heed Party commands".Split(' '),
        ];
        string[] hypothesis = "It is to insure the troops forever hearing the activity guidebook that party direct".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.12069, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUExampleB()
    {
        string[][] references = [
            "he was interested in world history because he read the book".Split(' '),
        ];
        string[] hypothesis = "he read the book because he was interested in world history".Split(' ');

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.7895, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUExampleAWithWordTokenizer()
    {
        string[][] references = [
            SimpleWordTokenizer.WordTokenize("It is a guide to action that ensures that the military will forever heed Party commands").ToArray(),
            SimpleWordTokenizer.WordTokenize("It is the guiding principle which guarantees the military forces always being under the command of the Party").ToArray(),
            SimpleWordTokenizer.WordTokenize("It is the practical guide for the army always to heed the directions of the party").ToArray(),
        ];
        string[] hypothesis = SimpleWordTokenizer.WordTokenize("It is a guide to action which ensures that the military always obeys the commands of the party").ToArray();

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.45454, score, 4);

    }

    [Fact]
    public void TestSentenceGLEUExampleBWithWordTokenizer()
    {
        string[][] references = [
            SimpleWordTokenizer.WordTokenize("he was interested in world history because he read the book").ToArray(),
        ];
        string[] hypothesis = SimpleWordTokenizer.WordTokenize("he read the book because he was interested in world history").ToArray();

        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.7895, score, 4);
    }

    [Fact]
    public void TestSentenceGLEUCatExample()
    {
        string[][] references = [
            "the cat is on the mat".Split(' '),
        ];
        string[] hypothesis = "the the the the the the the".Split(' ');
        double score = SentenceGLEU(references, hypothesis);
        Assert.Equal(0.0909, score, 4);
    }
}
