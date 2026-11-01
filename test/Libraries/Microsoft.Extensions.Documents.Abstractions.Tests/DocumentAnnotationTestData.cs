// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;

namespace Microsoft.Extensions.Documents.Tests;

internal static class DocumentAnnotationTestData
{
    internal const string RawBody = "{\"unknown\":{\"z\":[3,1]},\"marker\":\"RAW_BODY_ONLY_91\",\"pages\":[7,2]}";

    internal static byte[] OriginalBytes() => Encoding.UTF8.GetBytes(RawBody);

    internal static ExtractionDocumentPoint[] Polygon() =>
    [
        new(10.25, 20.5), new(40.75, 18.25), new(44.5, 60.125),
        new(12.875, 58.75), new(8.5, 35.25)
    ];

    internal static ExtractionDocumentSourcePage PdfPage(ExtractionDocumentSourceOutput? output = null) =>
        new("urn:frame:pdf-A", 7, new ExtractionDocumentPageDimensions(200, 100),
            ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft, output);

    internal sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class BorrowedNative : IDisposable
    {
        internal string Marker { get; } = "NATIVE_ONLY_92";

        internal int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
