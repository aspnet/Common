// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Utilities;

internal static class IterationNameComparer
{
    internal static Comparer<string> Default { get; } =
         Comparer<string>.Create(
             (first, second) =>
             {
                 int comparison;

                 if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int firstInteger) &&
                     int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondInteger))
                 {
                     comparison = firstInteger.CompareTo(secondInteger);
                 }
                 else if (
                     double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double firstDouble) &&
                     double.TryParse(second, NumberStyles.Float, CultureInfo.InvariantCulture, out double secondDouble))
                 {
                     comparison = firstDouble.CompareTo(secondDouble);
                 }
                 else
                 {
                     return string.Compare(first, second, StringComparison.Ordinal);
                 }

                 return comparison is 0 ? string.Compare(first, second, StringComparison.Ordinal) : comparison;
             });
}
