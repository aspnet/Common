// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Gen.Logging.Model;
using Microsoft.Gen.Shared;

namespace Microsoft.Gen.Logging.Emission;

// Stryker disable all

internal sealed partial class Emitter : EmitterBase
{
    private const string DataClassificationSetType = "global::Microsoft.Extensions.Compliance.Classification.DataClassificationSet";
    private const string GlobalNamespacePrefix = "global::";
    private const string LoggerMessageHelperType = "global::Microsoft.Extensions.Logging.LoggerMessageHelper";

    private readonly StringBuilderPool _sbPool = new();
    private readonly Dictionary<HashSet<string>, string> _classificationMap = new(HashSet<string>.CreateSetComparer());

    public string Emit(IEnumerable<LoggingType> logTypes, CancellationToken cancellationToken)
    {
        foreach (var lt in logTypes.OrderBy(static lt => lt.Namespace + "." + lt.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenType(lt);
        }

        return Capture();
    }

    private void GenType(LoggingType lt)
    {
        if (!string.IsNullOrWhiteSpace(lt.Namespace))
        {
            OutLn();
            OutLn($"namespace {lt.Namespace}");
            OutOpenBrace();
        }

        var parent = lt.Parent;
        var parentTypes = new List<string>();

        // loop until you find top level nested class
        while (parent != null)
        {
            parentTypes.Add($"partial {parent.Keyword} {parent.Name}");
            parent = parent.Parent;
        }

        // write down top level nested class first
        for (int i = parentTypes.Count - 1; i >= 0; i--)
        {
            OutLn(parentTypes[i]);
            OutOpenBrace();
        }

        OutLn($"partial {lt.Keyword} {lt.Name}");
        OutOpenBrace();

        GenAttributeClassifications(lt);

        var first = true;
        foreach (LoggingMethod lm in lt.Methods)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                OutLn();
            }

            GenLogMethod(lm);
        }

        OutCloseBrace();

        parent = lt.Parent;
        while (parent != null)
        {
            OutCloseBrace();
            parent = parent.Parent;
        }

        if (!string.IsNullOrWhiteSpace(lt.Namespace))
        {
            OutCloseBrace();
        }
    }

    private void GenAttributeClassifications(LoggingType lt)
    {
        // gather all the classifications referenced by the logging type
        var classificationSets = new List<HashSet<string>>();
        foreach (var lm in lt.Methods)
        {
            foreach (var parameter in lm.Parameters)
            {
                if (parameter.HasProperties)
                {
                    parameter.TraverseParameterPropertiesTransitively((_, property) => classificationSets.Add(property.ClassificationAttributeTypes));
                }
                else
                {
                    classificationSets.Add(parameter.ClassificationAttributeTypes);
                }
            }
        }

        _classificationMap.Clear();
        foreach (var classificationSet in classificationSets)
        {
            if (classificationSet.Count == 0 || _classificationMap.ContainsKey(classificationSet))
            {
                continue;
            }

            var classificationAttrs = classificationSet.OrderBy(static a => a, StringComparer.Ordinal).ToList();
            var fieldTypeNames = classificationAttrs.Select(static a => EncodeTypeName(a.StartsWith(GlobalNamespacePrefix, StringComparison.Ordinal) ? a.Substring(GlobalNamespacePrefix.Length) : a));
            var fieldName = PickUniqueName($"_{string.Join("_", fieldTypeNames)}", lt.AllMembers.Concat(_classificationMap.Values));
            var classifications = string.Join(", ", classificationAttrs.Select(static a => $"new {a}().Classification"));
            _classificationMap.Add(classificationSet, fieldName);

            OutGeneratedCodeAttribute();
            OutLn($"private static readonly {DataClassificationSetType} {fieldName} = new {DataClassificationSetType}({classifications});");
            OutLn();
        }
    }
}
