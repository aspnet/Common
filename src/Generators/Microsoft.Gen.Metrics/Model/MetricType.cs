// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Gen.Metrics.Model;

internal sealed class MetricType
{
    public readonly List<MetricMethod> Methods = [];
    public string Namespace = string.Empty;
    public string Name = string.Empty;
    public string Constraints = string.Empty;
    public string Modifiers = string.Empty;
    public string Keyword = string.Empty;
    public MetricType? Parent;
    public List<KeyValuePair<string, string>>? CommonTags;
}
