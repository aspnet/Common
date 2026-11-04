// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Gen.Metrics.Model;

namespace Microsoft.Gen.Metrics;

internal sealed class StrongTypeAttributeParameters
{
    public string MetricNameFromAttribute = string.Empty;
    public string MetricUnitFromAttribute = string.Empty;
    public string MetricDescriptionFromAttribute = string.Empty;
    public HashSet<string> TagHashSet = [];
    public Dictionary<string, string> TagDescriptionDictionary = [];
    public List<StrongTypeConfig> StrongTypeConfigs = [];
    public string StrongTypeObjectName = string.Empty;
    public bool IsClass;
}
