// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using static Microsoft.Extensions.AI.Evaluation.Reporting.Storage.AzureStorageResponseCache;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.JsonSerialization;

internal static partial class AzureStorageJsonUtilities
{
    internal static class Default
    {
        internal static JsonSerializerOptions Options => field ??= CreateJsonSerializerOptions(writeIndented: true);
        internal static JsonTypeInfo<CacheEntry> CacheEntryTypeInfo => Options.GetTypeInfo<CacheEntry>();
        internal static JsonTypeInfo<ScenarioRunResult> ScenarioRunResultTypeInfo => Options.GetTypeInfo<ScenarioRunResult>();
    }

    internal static class Compact
    {
        internal static JsonSerializerOptions Options => field ??= CreateJsonSerializerOptions(writeIndented: false);
        internal static JsonTypeInfo<CacheEntry> CacheEntryTypeInfo => Options.GetTypeInfo<CacheEntry>();
        internal static JsonTypeInfo<ScenarioRunResult> ScenarioRunResultTypeInfo => Options.GetTypeInfo<ScenarioRunResult>();
    }

#if !NET11_0_OR_GREATER
    private static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
#endif

    private static JsonSerializerOptions CreateJsonSerializerOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions(JsonContext.Default.Options)
        {
            WriteIndented = writeIndented,
        };
        options.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);
        options.MakeReadOnly();
        return options;
    }

    [JsonSerializable(typeof(ScenarioRunResult))]
    [JsonSerializable(typeof(CacheEntry))]
    [JsonSourceGenerationOptions(
        Converters = [
            typeof(CamelCaseEnumConverter<EvaluationDiagnosticSeverity>),
            typeof(CamelCaseEnumConverter<EvaluationRating>),
            typeof(TimeSpanConverter),
            typeof(EvaluationContextConverter)
        ],
        WriteIndented = true,
        IgnoreReadOnlyProperties = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    private sealed partial class JsonContext : JsonSerializerContext;
}
