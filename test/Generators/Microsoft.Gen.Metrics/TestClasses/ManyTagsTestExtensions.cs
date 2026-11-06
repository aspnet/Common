// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace TestClasses
{
#pragma warning disable SA1402 // File may only contain a single type
    internal static partial class ManyTagsTestExtensions
    {
        [Counter(typeof(ManyTagsDimensions))]
        public static partial ManyTagsCounter CreateManyTagsCounter(Meter meter);
    }

    public class ManyTagsDimensions
    {
        public string D1 { get; set; } = string.Empty;
        public string D2 { get; set; } = string.Empty;
        public string D3 { get; set; } = string.Empty;
        public string D4 { get; set; } = string.Empty;
        public string D5 { get; set; } = string.Empty;
        public string D6 { get; set; } = string.Empty;
        public string D7 { get; set; } = string.Empty;
        public string D8 { get; set; } = string.Empty;
        public string D9 { get; set; } = string.Empty;
        public string D10 { get; set; } = string.Empty;
        public string D11 { get; set; } = string.Empty;
        public string D12 { get; set; } = string.Empty;
        public string D13 { get; set; } = string.Empty;
        public string D14 { get; set; } = string.Empty;
        public string D15 { get; set; } = string.Empty;
        public string D16 { get; set; } = string.Empty;
        public string D17 { get; set; } = string.Empty;
        public string D18 { get; set; } = string.Empty;
        public string D19 { get; set; } = string.Empty;
        public string D20 { get; set; } = string.Empty;
        public string D21 { get; set; } = string.Empty;
        public string D22 { get; set; } = string.Empty;
        public string D23 { get; set; } = string.Empty;
        public string D24 { get; set; } = string.Empty;
        public string D25 { get; set; } = string.Empty;
        public string D26 { get; set; } = string.Empty;
        public string D27 { get; set; } = string.Empty;
        public string D28 { get; set; } = string.Empty;
        public string D29 { get; set; } = string.Empty;
        public string D30 { get; set; } = string.Empty;
        public string D31 { get; set; } = string.Empty;
        public string D32 { get; set; } = string.Empty;
        public string D33 { get; set; } = string.Empty;
    }
#pragma warning restore SA1402
}
