// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace TestClasses
{
    [SuppressMessage("Usage", "CA1801:Review unused parameters",
        Justification = "Method body is source generated where the parameters will be used")]
    public static partial class MetricsWithDescription
    {
        [Counter(Description = "Number of requests processed.")]
        public static partial CounterWithDescription CreateCounterWithDescription(Meter meter);

        [Counter("s1", "s2", Unit = "{request}", Description = "Number of requests processed, by dimension.", Name = "CounterWithUnitAndDescription")]
        public static partial CounterWithUnitAndDescription CreateCounterWithUnitAndDescription(Meter meter);

        [Counter(typeof(Dimensions), Description = "Strongly-typed counter.")]
        public static partial CounterStrongTypeWithDescription CreateCounterStrongTypeWithDescription(Meter meter);

        [Counter<double>(Description = "Generic counter.", Name = "GenericDoubleCounterWithDescription")]
        public static partial GenericDoubleCounterWithDescription CreateGenericDoubleCounterWithDescription(Meter meter);

        [Counter(Description = "")]
        public static partial CounterWithEmptyDescription CreateCounterWithEmptyDescription(Meter meter);

        [Histogram(Unit = "ms", Description = "Duration of the operation.", Name = "HistogramWithUnitAndDescription")]
        public static partial HistogramWithUnitAndDescription CreateHistogramWithUnitAndDescription(Meter meter);

        [Histogram(typeof(Dimensions), Description = "Strongly-typed histogram.")]
        public static partial HistogramStrongTypeWithDescription CreateHistogramStrongTypeWithDescription(Meter meter);

        [Histogram<int>("s1", Description = "Contains \"quotes\", a backslash \\, {braces}\tand a\nnew line.", Name = "GenericIntHistogramWithEscapedDescription")]
        public static partial GenericIntHistogramWithEscapedDescription CreateGenericIntHistogramWithEscapedDescription(Meter meter);
    }
}
