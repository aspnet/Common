// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Diagnostics.Sampling;
using Xunit;

namespace Microsoft.Extensions.Telemetry.Sampling;

public class BottomKSamplerTests
{
    [Fact]
    public void Chao1UnseenWeight_HandlesDegenerateAndObservedDistributions()
    {
        Assert.Equal(1.0, ChaoEstimator.Chao1UnseenWeight([]));
        Assert.Equal(1.0, ChaoEstimator.Chao1UnseenWeight([0]));
        Assert.Equal(1.0, ChaoEstimator.Chao1UnseenWeight([2]));
        Assert.Equal(1.0, ChaoEstimator.Chao1UnseenWeight([1]));
        Assert.Equal(0.25, ChaoEstimator.Chao1UnseenWeight([1, 1]), 12);
        Assert.Equal(1.0 / 3.0, ChaoEstimator.Chao1UnseenWeight([1, 2]), 12);
    }

    [Fact]
    public void Admission_ImplementsValueEquality()
    {
        Admission first = Admission.Admit(1.5);
        Admission same = Admission.Admit(1.5);
        Admission different = Admission.Admit(2.5);
        object boxed = same;
        object otherType = "other";

        Assert.True(first == same);
        Assert.False(first != same);
        Assert.True(first != different);
        Assert.True(first.Equals(boxed));
        Assert.False(first.Equals(Admission.Skip));
        Assert.False(first.Equals(otherType));
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void SampledRecord_ImplementsValueEquality()
    {
        var first = new SampledRecord<int, string>(1, "payload", 2.0);
        var same = new SampledRecord<int, string>(1, "payload", 2.0);
        var differentCallsite = new SampledRecord<int, string>(2, "payload", 2.0);
        var differentPayload = new SampledRecord<int, string>(1, "other", 2.0);
        var differentCount = new SampledRecord<int, string>(1, "payload", 3.0);
        object boxed = same;
        object otherType = "other";

        Assert.True(first == same);
        Assert.False(first != same);
        Assert.True(first != differentCallsite);
        Assert.True(first != differentPayload);
        Assert.True(first != differentCount);
        Assert.True(first.Equals(boxed));
        Assert.False(first.Equals(otherType));
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void DefaultConstructor_UsesDefaultConfiguration()
    {
        var sampler = new BottomKSampler<int, int>(1);

        Add(sampler, 1, 1);

        Assert.Equal(0, sampler.FrozenCallsites);
        Assert.Equal(1.0, sampler.UnseenWeight);
        Assert.Single(sampler.Flush());
        Assert.Equal(0, sampler.FrozenCallsites);
    }

    [Fact]
    public void Insert_RejectsSkipAdmission()
    {
        var sampler = new BottomKSampler<int, int>(1);

        Assert.Throws<ArgumentException>(() => sampler.Insert(1, Admission.Skip, 1));
    }

    [Fact]
    public void Flush_WithSmallThreshold_UsesStableExponentialCalculation()
    {
        var sampler = new BottomKSampler<int, int>(1, 0, 0, BottomKUnseenWeightMode.RarestSeen, 42);
        sampler.Insert(1, Admission.Admit(1e-7), 1);
        sampler.Insert(1, Admission.Admit(2e-7), 2);

        SampledRecord<int, int> record = Assert.Single(sampler.Flush());

        Assert.True(double.IsFinite(record.SamplingCount));
        Assert.True(record.SamplingCount > 1.0);
    }

    [Fact]
    public void Chao1Mode_UsesEstimatorAfterPeriodFlush()
    {
        var sampler = new BottomKSampler<int, int>(4, 0, 0, BottomKUnseenWeightMode.Chao1, 42);
        Add(sampler, 1, 1);
        Add(sampler, 2, 2);

        _ = sampler.Flush();

        Assert.Equal(0.25, sampler.UnseenWeight, 12);
    }

    [Fact]
    public void Flush_UsesFirstExcludedRankForSamplingCount()
    {
        const int Seed = 42;
        var sampler = new BottomKSampler<int, int>(1, 0, 0, BottomKUnseenWeightMode.RarestSeen, Seed);

        Add(sampler, 0, 0);
        Add(sampler, 0, 1);

        SampledRecord<int, int> record = Assert.Single(sampler.Flush());

        var random = new Random(Seed);
        double firstRank = -Math.Log(random.NextDouble());
        double secondRank = -Math.Log(random.NextDouble());
        double firstExcludedRank = Math.Max(firstRank, secondRank);
        double expectedWeight = 1.0 / (1.0 - Math.Exp(-firstExcludedRank));

        Assert.Equal(expectedWeight, record.SamplingCount, 12);
        Assert.Equal(firstRank < secondRank ? 0 : 1, record.Payload);
    }

    [Fact]
    public void Flush_WhenInputFitsCapacity_UsesUnitWeights()
    {
        var sampler = new BottomKSampler<int, int>(4, 0, 0, BottomKUnseenWeightMode.RarestSeen, 42);

        Add(sampler, 0, 0);
        Add(sampler, 1, 1);
        Add(sampler, 2, 2);

        List<SampledRecord<int, int>> records = sampler.Flush();

        Assert.Equal(3, records.Count);
        Assert.All(records, record => Assert.Equal(1.0, record.SamplingCount));
    }

    [Theory]
    [InlineData(1, 0.08)]
    [InlineData(8, 0.03)]
    [InlineData(32, 0.02)]
    [InlineData(128, 0.01)]
    public void SamplingCount_MeanConvergesToUniformInput(int capacity, double tolerance)
    {
        const int Arrivals = 256;
        const int Trials = 4_000;
        double estimatedTotal = 0.0;

        for (int seed = 0; seed < Trials; seed++)
        {
            var sampler = new BottomKSampler<int, int>(capacity, 0, 0, BottomKUnseenWeightMode.RarestSeen, seed);
            for (int i = 0; i < Arrivals; i++)
            {
                Add(sampler, 0, i);
            }

            List<SampledRecord<int, int>> records = sampler.Flush();
            Assert.All(records, record =>
            {
                Assert.True(double.IsFinite(record.SamplingCount));
                Assert.True(record.SamplingCount >= 1.0);
            });

            estimatedTotal += records.Sum(record => record.SamplingCount);
        }

        double mean = estimatedTotal / Trials;
        Assert.InRange(mean, Arrivals * (1.0 - tolerance), Arrivals * (1.0 + tolerance));
    }

    [Fact]
    public void SamplingCount_MeanConvergesForSkewedKnownCallsites()
    {
        const int Trials = 2_000;
        int[] arrivals = [900, 90, 10];
        double[] estimatedByCallsite = new double[arrivals.Length];

        for (int seed = 0; seed < Trials; seed++)
        {
            var sampler = new BottomKSampler<int, int>(32, 0, 0, BottomKUnseenWeightMode.RarestSeen, seed);

            AddPeriod(sampler, arrivals);
            _ = sampler.Flush();

            AddPeriod(sampler, arrivals);
            foreach (SampledRecord<int, int> record in sampler.Flush())
            {
                estimatedByCallsite[record.Callsite] += record.SamplingCount;
            }
        }

        for (int callsite = 0; callsite < arrivals.Length; callsite++)
        {
            double mean = estimatedByCallsite[callsite] / Trials;
            Assert.InRange(mean, arrivals[callsite] * 0.9, arrivals[callsite] * 1.1);
        }
    }

    [Fact]
    public void Insert_RetainsKPlusOneAndReleasesLargerRanks()
    {
        List<int> released = [];
        var sampler = new BottomKSampler<int, int>(
            1,
            0,
            0,
            BottomKUnseenWeightMode.RarestSeen,
            42,
            released.Add);

        sampler.Insert(0, Admission.Admit(3.0), 30);
        sampler.Insert(0, Admission.Admit(2.0), 20);

        Assert.Equal(3.0, sampler.Tau);
        Assert.Empty(released);

        sampler.Insert(0, Admission.Admit(1.0), 10);

        Assert.Equal(2.0, sampler.Tau);
        Assert.Equal([30], released);

        SampledRecord<int, int> retained = Assert.Single(sampler.Flush());
        Assert.Equal(10, retained.Payload);
        Assert.Equal([30, 20], released);
    }

    [Fact]
    public void Insert_StalePreserveAdmission_ReleasesUnretainedPayload()
    {
        List<int> released = [];
        var sampler = new BottomKSampler<int, int>(
            1,
            1,
            0,
            BottomKUnseenWeightMode.RarestSeen,
            42,
            released.Add);

        sampler.Insert(1, Admission.Preserve, 10);
        sampler.Insert(1, Admission.Preserve, 20);

        Assert.Equal([20], released);
        SampledRecord<int, int> retained = Assert.Single(sampler.Flush());
        Assert.Equal(10, retained.Payload);
        Assert.Equal(0.0, retained.SamplingCount);
    }

    [Fact]
    public void Insert_HeapAdmissionSupplantsPreserveAndReleasesItsPayload()
    {
        List<int> released = [];
        var sampler = new BottomKSampler<int, int>(
            1,
            1,
            0,
            BottomKUnseenWeightMode.RarestSeen,
            42,
            released.Add);

        sampler.Insert(1, Admission.Preserve, 10);
        sampler.Insert(1, Admission.Admit(1.0), 20);

        Assert.Equal([10], released);
        SampledRecord<int, int> retained = Assert.Single(sampler.Flush());
        Assert.Equal(20, retained.Payload);
    }

    private static void AddPeriod(BottomKSampler<int, int> sampler, int[] arrivals)
    {
        for (int callsite = 0; callsite < arrivals.Length; callsite++)
        {
            for (int i = 0; i < arrivals[callsite]; i++)
            {
                Add(sampler, callsite, i);
            }
        }
    }

    private static void Add(BottomKSampler<int, int> sampler, int callsite, int payload)
    {
        Admission admission = sampler.Admit(callsite);
        if (admission.Kind != AdmissionKind.Skip)
        {
            sampler.Insert(callsite, admission, payload);
        }
    }
}
#endif
