// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace TestClasses
{
#pragma warning disable SA1402 // File may only contain a single type
    internal static partial class MetricsWithPrimitiveTags
    {
        [Counter(typeof(PrimitiveTags), Name = "CounterWithPrimitiveTags")]
        public static partial CounterWithPrimitiveTags CreateCounterWithPrimitiveTags(Meter meter);

        [Histogram<double>(typeof(PrimitiveTagsStruct), Name = "HistogramWithPrimitiveTags")]
        public static partial HistogramWithPrimitiveTags CreateHistogramWithPrimitiveTags(this Meter meter);

        [Counter(typeof(PrimitiveTagsRecordStruct), Name = "CounterWithPrimitiveTagsRecordStruct")]
        public static partial CounterWithPrimitiveTagsRecordStruct CreateCounterWithPrimitiveTagsRecordStruct(Meter meter);
    }

    public class PrimitiveTags
    {
        // The generator should ignore these statics:
        public const int Const = 1;

        public static int Static = 2;

        [TagName("server.port")]
        public int Port { get; set; }

        [TagName("http.response.status_code")]
        public int StatusCode;

        public bool BoolTag { get; set; }

        public sbyte SByteTag { get; set; }

        public byte ByteTag { get; set; }

        public short ShortTag { get; set; }

        public ushort UShortTag { get; set; }

        public uint UIntTag { get; set; }

        public long LongTag { get; set; }

        public float FloatTag { get; set; }

        public double DoubleTag { get; set; }

        public string? StringTag { get; set; }

        public PrimitiveTagsChild? Child { get; set; }
    }

    public class PrimitiveTagsChild
    {
        [TagName("child.port")]
        public int ChildPort { get; set; }
    }

    public struct PrimitiveTagsStruct
    {
        [TagName("server.port")]
        public int Port { get; set; }

        public bool Flag { get; set; }

        public double Ratio;

        public PrimitiveTagsNestedStruct Nested { get; set; }
    }

    public struct PrimitiveTagsNestedStruct
    {
        public long NestedLong { get; set; }
    }

    public readonly record struct PrimitiveTagsRecordStruct(
        [property: TagName("http.request.method")] string Method,
        [property: TagName("http.response.status_code")] int StatusCode,
        [property: TagName("server.port")] int Port);
#pragma warning restore SA1402 // File may only contain a single type
}
