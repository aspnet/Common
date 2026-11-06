// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Microsoft.Gen.Metrics;

[ExcludeFromCodeCoverage]
internal sealed record class SymbolHolder(
    INamedTypeSymbol MeterSymbol,
    INamedTypeSymbol CounterAttribute,
    INamedTypeSymbol? CounterOfTAttribute,
    INamedTypeSymbol HistogramAttribute,
    INamedTypeSymbol? HistogramOfTAttribute,
    INamedTypeSymbol? GaugeAttribute,
    INamedTypeSymbol? GaugeOfTAttribute,
    INamedTypeSymbol LongTypeSymbol,
    INamedTypeSymbol? TagNameAttribute,
    INamedTypeSymbol? CommonTagAttribute);
