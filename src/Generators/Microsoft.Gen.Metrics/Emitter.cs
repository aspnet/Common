// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Gen.Metrics.Model;
using Microsoft.Gen.Shared;

namespace Microsoft.Gen.Metrics;

// Stryker disable all

internal sealed class Emitter : EmitterBase
{
    private static readonly Regex _regex = new("[:.-]+", RegexOptions.Compiled);

    public string EmitMetrics(IReadOnlyList<MetricType> metricTypes, CancellationToken cancellationToken)
    {
        Dictionary<string, List<MetricType>> metricClassesDict = [];
        foreach (var cl in metricTypes)
        {
            if (!metricClassesDict.TryGetValue(cl.Namespace, out var list))
            {
                list = [];
                metricClassesDict.Add(cl.Namespace, list);
            }

            list.Add(cl);
        }

        foreach (var entry in metricClassesDict.OrderBy(static x => x.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenTypeByNamespace(entry.Key, entry.Value, cancellationToken);
        }

        return Capture();
    }

    private static string GetSanitizedParamName(string paramName)
    {
        return _regex.Replace(paramName, "_");
    }

    private void GenTypeByNamespace(string nspace, IEnumerable<MetricType> metricTypes, CancellationToken cancellationToken)
    {
        OutLn();
        if (!string.IsNullOrWhiteSpace(nspace))
        {
            OutLn($"namespace {nspace}");
            OutOpenBrace();
        }

        var first = true;
        foreach (var metricClass in metricTypes.OrderBy(static x => x.Name))
        {
            if (first)
            {
                first = false;
            }
            else
            {
                OutLn();
            }

            cancellationToken.ThrowIfCancellationRequested();
            GenType(metricClass, nspace);
        }

        if (!string.IsNullOrWhiteSpace(nspace))
        {
            OutCloseBrace();
        }
    }

    private void GenType(MetricType metricType, string nspace)
    {
        GenInstrumentCreateMethods(metricType, nspace);
        OutLn();

        var first = true;
        foreach (var metricMethod in metricType.Methods)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                OutLn();
            }

            GenInstrumentClass(metricMethod);
        }
    }

    private void GenInstrumentCreateMethods(MetricType metricType, string nspace)
    {
        var parent = metricType.Parent;
        var parentTypes = new List<string>();

        // loop until you find top level nested class
        while (parent != null)
        {
            parentTypes.Add($"{parent.Modifiers} {parent.Keyword} {parent.Name} {parent.Constraints}");
            parent = parent.Parent;
        }

        // write down top level nested class first
        for (int i = parentTypes.Count - 1; i >= 0; i--)
        {
            OutLn(parentTypes[i]);
            OutOpenBrace();
        }

        OutGeneratedCodeAttribute();
        OutLn($"{metricType.Modifiers} {metricType.Keyword} {metricType.Name} {metricType.Constraints}");
        OutOpenBrace();
        var first = true;
        foreach (var metricMethod in metricType.Methods)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                OutLn();
            }

            GenInstrumentCreateMethod(metricMethod, nspace);
        }

        OutCloseBrace();

        parent = metricType.Parent;
        while (parent != null)
        {
            OutCloseBrace();
            parent = parent.Parent;
        }
    }

    private void GenInstrumentClass(MetricMethod metricMethod)
    {
        const string CounterObjectName = "counter";
        const string HistogramObjectName = "histogram";
        const string GaugeObjectName = "gauge";

        const string CounterRecordStatement = "Add";
        const string HistogramRecordStatement = "Record";
        const string GaugeRecordStatement = "Record";

        const string CounterOfTTypeDefinitionTemplate = "global::System.Diagnostics.Metrics.Counter<{0}>";
        const string HistogramOfTTypeDefinitionTemplate = "global::System.Diagnostics.Metrics.Histogram<{0}>";
        const string GaugeOfTTypeDefinitionTemplate = "global::System.Diagnostics.Metrics.Gauge<{0}>";

        string objectName;
        string typeDefinition;
        string recordStatement;
        string metricValueType = metricMethod.GenericType;

        switch (metricMethod.InstrumentKind)
        {
            case InstrumentKind.Counter:
            case InstrumentKind.CounterT:
                recordStatement = CounterRecordStatement;
                typeDefinition = string.Format(CultureInfo.InvariantCulture, CounterOfTTypeDefinitionTemplate, metricValueType);
                objectName = CounterObjectName;
                break;
            case InstrumentKind.Histogram:
            case InstrumentKind.HistogramT:
                recordStatement = HistogramRecordStatement;
                typeDefinition = string.Format(CultureInfo.InvariantCulture, HistogramOfTTypeDefinitionTemplate, metricValueType);
                objectName = HistogramObjectName;
                break;
            case InstrumentKind.Gauge:
            case InstrumentKind.GaugeT:
                recordStatement = GaugeRecordStatement;
                typeDefinition = string.Format(CultureInfo.InvariantCulture, GaugeOfTTypeDefinitionTemplate, metricValueType);
                objectName = GaugeObjectName;
                break;
            default:
                throw new NotSupportedException($"Instrument type '{metricMethod.InstrumentKind}' is not supported to generate metric");
        }

        var tagListInit = metricMethod.TagKeys.Count != 0 ||
                          metricMethod.StrongTypeConfigs.Count != 0;

        var accessModifier = metricMethod.MetricTypeModifiers.Contains("public")
            ? "public"
            : "internal";

        OutGeneratedCodeAttribute();
        OutLn($"{accessModifier} sealed class {metricMethod.MetricTypeName}");
        OutLn($"{{");
        OutLn($"    private readonly {typeDefinition} _{objectName};");
        OutLn();
        OutLn($"    public {metricMethod.MetricTypeName}({typeDefinition} {objectName})");
        OutLn($"    {{");
        OutLn($"        _{objectName} = {objectName};");
        OutLn($"    }}");
        OutLn();

        OutIndent();
        Out($"    public void {recordStatement}({metricValueType} value");
        GenTagsParameters(metricMethod);
        Out(")");
        OutLn();

        OutLn($"    {{");

        const int MaxTagsWithoutEnabledCheck = 8;
        if (metricMethod.TagKeys.Count > MaxTagsWithoutEnabledCheck ||
            metricMethod.StrongTypeConfigs.Count > MaxTagsWithoutEnabledCheck)
        {
            OutLn($"        if (!_{objectName}.Enabled)");
            OutLn($"        {{");
            OutLn($"            return;");
            OutLn($"        }}");
            OutLn();
        }

        if (metricMethod.IsTagTypeClass)
        {
            OutLn($"        if (o == null)");
            OutLn($"        {{");
            OutLn($"            throw new global::System.ArgumentNullException(nameof(o));");
            OutLn($"        }}");
            OutLn();
        }

        if (tagListInit)
        {
            Indent();
            Indent();
            OutLn("var tagList = new global::System.Diagnostics.TagList");
            OutOpenBrace();
            GenTagList(metricMethod);
            Unindent();
            OutLn("};");
            Unindent();
            Unindent();
            OutLn();
        }

        OutLn($"        _{objectName}.{recordStatement}(value{(tagListInit ? ", tagList" : string.Empty)});");
        OutLn($"    }}");
        OutLn("}");
    }

    private void GenInstrumentCreateMethod(MetricMethod metricMethod, string nspace)
    {
        var nsprefix = string.IsNullOrWhiteSpace(nspace)
            ? string.Empty
            : $"global::{nspace}.";

        var thisModifier = metricMethod.IsExtensionMethod
                ? "this "
                : string.Empty;

        var meterParam = metricMethod.AllParameters[0];
        var accessModifier = metricMethod.Modifiers.Contains("public")
                ? "public"
                : "internal";

        OutGeneratedCodeAttribute();
        OutIndent();
        Out($"{accessModifier} static partial {nsprefix}{metricMethod.MetricTypeName} {metricMethod.Name}({thisModifier}");
        foreach (var p in metricMethod.AllParameters)
        {
            if (p != meterParam)
            {
                Out(", ");
            }

            Out($"{p.Type} {p.Name}");
        }

        Out(")");
        OutLn();
        OutIndent();
        Out($"    => {nsprefix}GeneratedInstrumentsFactory.Create{metricMethod.MetricTypeName}(");
        foreach (var p in metricMethod.AllParameters)
        {
            if (p != meterParam)
            {
                Out(", ");
            }

            Out(p.Name);
        }

        Out(");");
        OutLn();
    }

    private void GenTagList(MetricMethod metricMethod)
    {
        if (string.IsNullOrEmpty(metricMethod.StrongTypeObjectName))
        {
            foreach (var tagName in metricMethod.TagKeys)
            {
                var paramName = GetSanitizedParamName(tagName);
                OutLn($"new global::System.Collections.Generic.KeyValuePair<string, object?>(\"{tagName}\", {paramName}),");
            }
        }
        else
        {
            foreach (var config in metricMethod.StrongTypeConfigs)
            {
                var paramName = GetSanitizedParamName(config.Name);
                var paramInvoke = config.StrongTypeMetricObjectType switch
                {
                    StrongTypeMetricObjectType.String => $"{paramName}!",
                    StrongTypeMetricObjectType.Enum => $"{paramName}.ToString()",
                    StrongTypeMetricObjectType.Primitive => paramName,
                    _ => null,
                };

                if (paramInvoke is null)
                {
                    continue;
                }

                var access = string.IsNullOrEmpty(config.Path)
                    ? "o." + paramInvoke
                    : "o." + config.Path + "." + paramInvoke;

                OutLn($"new global::System.Collections.Generic.KeyValuePair<string, object?>(\"{config.TagName}\", {access}),");
            }
        }
    }

    private void GenTagsParameters(MetricMethod metricMethod)
    {
        if (string.IsNullOrEmpty(metricMethod.StrongTypeObjectName))
        {
            foreach (var tagName in metricMethod.TagKeys)
            {
                var paramName = GetSanitizedParamName(tagName);
                Out($", object? {paramName}");
            }
        }
        else
        {
            Out($", global::{metricMethod.StrongTypeObjectName} o");
        }
    }
}
