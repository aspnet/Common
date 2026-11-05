// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Gen.Logging.Model;
using Microsoft.Gen.Shared;

namespace Microsoft.Gen.Logging.Emission;

// Stryker disable all

internal sealed partial class Emitter : EmitterBase
{
#pragma warning disable CA1505 // Avoid unmaintainable code
    private void GenLogMethod(LoggingMethod lm)
#pragma warning restore CA1505
    {
        string level = GetLoggerMethodLogLevel(lm);
        string extension = lm.IsExtensionMethod ? "this " : string.Empty;
        string eventName = string.IsNullOrWhiteSpace(lm.EventName) ? $"nameof({lm.Name})" : $"\"{lm.EventName}\"";
        (string exceptionArg, string exceptionLambdaName) = GetException(lm);
        (string logger, bool isNullableLogger) = GetLogger(lm);

        if (!lm.HasXmlDocumentation)
        {
            var l = GetLoggerMethodLogLevelForXmlDocumentation(lm);
            var lvl = string.Empty;
            if (l != null)
            {
                lvl = $" at \"{l}\" level";
            }

            OutLn($"/// <summary>");

            if (!string.IsNullOrEmpty(lm.Message))
            {
                OutLn($"/// Logs \"{EscapeMessageStringForXmlDocumentation(lm.Message)}\"{lvl}.");
            }
            else
            {
                OutLn($"/// Emits a structured log entry{lvl}.");
            }

            OutLn($"/// </summary>");
        }

        OutGeneratedCodeAttribute();

        OutIndent();
        Out($"{lm.Modifiers} void {lm.Name}");
        GenTypeParameterList(lm);
        Out($"({extension}");
        GenParameters(lm);
        Out(')');
        GenTypeConstraints(lm);
        OutLn();

        OutOpenBrace();

        if (isNullableLogger)
        {
            OutLn($"if ({logger} == null)");
            OutOpenBrace();
            OutLn("return;");
            OutCloseBrace();
            OutLn();
        }

        if (!lm.SkipEnabledCheck)
        {
            OutLn($"if (!{logger}.IsEnabled({level}))");
            OutOpenBrace();
            OutLn("return;");
            OutCloseBrace();
            OutLn();
        }

        var stateName = PickUniqueName("state", lm.Parameters.Select(p => p.ParameterName));

        OutLn($"var {stateName} = {LoggerMessageHelperType}.ThreadLocalState;");
        OutLn("try");
        OutOpenBrace();
        GenTagWrites(lm, stateName, out int numReservedUnclassifiedTags, out int numReservedClassifiedTags);

        OutLn();
        OutLn($"{logger}.Log(");

        Indent();
        OutLn($"{level},");

        if (lm.EventId != null)
        {
            OutLn($"new({lm.EventId}, {eventName}),");
        }
        else
        {
            var eventNameToCalcId = string.IsNullOrWhiteSpace(lm.EventName) ? lm.Name : lm.EventName!;
            var calculatedEventId = GetNonRandomizedHashCode(eventNameToCalcId);
            OutLn($"new({calculatedEventId}, {eventName}),");
        }

        OutLn($"{stateName},");
        OutLn($"{exceptionArg},");

        var lambdaStateName = PickUniqueName("s", lm.Templates);

        OutLn($"[{GeneratorUtilities.GeneratedCodeAttribute}] static string ({lambdaStateName}, {exceptionLambdaName}) =>");
        OutOpenBrace();

        if (!string.IsNullOrEmpty(lm.Message) &&
            GenVariableAssignments(lm, lambdaStateName, numReservedUnclassifiedTags, numReservedClassifiedTags))
        {
            var mapped = Parsing.TemplateProcessor.MapTemplates(lm.Message, t =>
            {
                var p = lm.GetParameterForTemplate(t);
                if (p != null)
                {
                    return p.ParameterNameWithAtIfNeeded;
                }

                return t;
            });

            var s = EscapeMessageString(mapped!);
            OutLn($@"#if NET");
            OutLn($@"return string.Create(global::System.Globalization.CultureInfo.InvariantCulture, ${s});");
            OutLn($@"#else");
            OutLn($@"return global::System.FormattableString.Invariant(${s});");
            OutLn($@"#endif");
        }
        else if (string.IsNullOrEmpty(lm.Message))
        {
            OutLn($@"return string.Empty;");
        }
        else
        {
            OutLn($@"return {EscapeMessageString(lm.Message)};");
        }

        OutCloseBraceWithExtra(");");
        Unindent();

        OutCloseBrace();
        OutLn("finally");
        OutOpenBrace();
        OutLn($"{stateName}.Clear();");
        OutCloseBrace();
        OutCloseBrace();

        /// <summary>
        /// Returns a non-randomized hash code for the given string.
        /// </summary>
        /// <remarks>
        /// We always return a positive value.
        /// This code is cloned from the logging generator in dotnet/runtime in
        /// order to retain the same event ids when upgrading to this generator.
        /// </remarks>
        static int GetNonRandomizedHashCode(string s)
        {
            const int Mult = 16_777_619;
            uint result = 2_166_136_261u;
            foreach (char c in s)
            {
                result = (c ^ result) * Mult;
            }

            int ret = (int)result;
            return ret == int.MinValue ? 0 : Math.Abs(ret); // Ensure the result is non-negative
        }

        static bool ShouldStringifyParameter(LoggingMethodParameter p)
        {
            if (p.IsReference)
            {
                // pass object as-is
                return false;
            }

            if (p.ImplementsISpanFormattable)
            {
                // pass object as-is, it will be formatted directly into the output buffer
                return false;
            }

            if (!p.Type.Contains("."))
            {
                // no . means this is a primitive type, pass as-is
                return false;
            }

            // should convert the object to a string before calling the Log function
            return true;
        }

        static bool ShouldStringifyProperty(LoggingProperty p)
        {
            if (p.IsReference)
            {
                // pass object as is
                return false;
            }

            if (p.ImplementsISpanFormattable)
            {
                // pass object as is, it will be formatted directly into the output buffer
                return false;
            }

            if (!p.Type.Contains("."))
            {
                // no . means this is a primitive type, pass as-is
                return false;
            }

            // should convert the object to a string before calling the Log function
            return true;
        }

        static string ConvertParameterToString(LoggingMethodParameter lp, string arg)
        {
            var question = lp.PotentiallyNull ? "?" : string.Empty;
            if (lp.ImplementsIConvertible)
            {
                return $"{arg}{question}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
            }
            else if (lp.ImplementsIFormattable)
            {
                return $"{arg}{question}.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)";
            }

            return $"{arg}{question}.ToString()";
        }

        static string ConvertPropertyToString(LoggingProperty lp, string arg)
        {
            var question = lp.PotentiallyNull ? "?" : string.Empty;
            if (lp.ImplementsIConvertible)
            {
                return $"{arg}{question}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
            }
            else if (lp.ImplementsIFormattable)
            {
                return $"{arg}{question}.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)";
            }

            return $"{arg}{question}.ToString()";
        }

        static (string exceptionArg, string exceptionLambdaArg) GetException(LoggingMethod lm)
        {
            string exceptionArg = "null";
            string exceptionLambdaArg = "_";

            foreach (var p in lm.Parameters)
            {
                if (p.IsException)
                {
                    exceptionArg = p.ParameterName;

                    if (p.UsedAsTemplate)
                    {
                        exceptionLambdaArg = lm.GetTemplatesForParameter(p)[0];
                    }

                    break;
                }
            }

            return (exceptionArg, exceptionLambdaArg);
        }

        static bool NeedsASlot(LoggingMethodParameter p)
        {
            if (p.UsedAsTemplate && !p.IsException)
            {
                return true;
            }

            return p.IsNormalParameter && !p.HasTagProvider && !p.HasProperties;
        }

        void GenTagWrites(LoggingMethod lm, string stateName, out int numReservedUnclassifiedTags, out int numReservedClassifiedTags)
        {
            int numUnclassifiedTags = 0;
            int numClassifiedTags = 0;
            var tmpVarName = PickUniqueName("tmp", lm.Parameters.Select(p => p.ParameterName));

            foreach (var p in lm.Parameters)
            {
                if (NeedsASlot(p))
                {
                    if (p.HasDataClassification)
                    {
                        numClassifiedTags++;
                    }
                    else
                    {
                        numUnclassifiedTags++;
                    }
                }

                if (p.HasProperties && !p.SkipNullProperties)
                {
                    p.TraverseParameterPropertiesTransitively((_, member) =>
                    {
                        if (member.HasDataClassification)
                        {
                            numClassifiedTags++;
                        }
                        else
                        {
                            numUnclassifiedTags++;
                        }
                    });
                }
            }

            if (!string.IsNullOrEmpty(lm.Message))
            {
                numUnclassifiedTags++;
            }

            numReservedUnclassifiedTags = numUnclassifiedTags;
            numReservedClassifiedTags = numClassifiedTags;

            if (numReservedUnclassifiedTags > 0)
            {
                OutLn();
                OutLn($"_ = {stateName}.ReserveTagSpace({numReservedUnclassifiedTags});");

                int count = numReservedUnclassifiedTags;

                if (!string.IsNullOrEmpty(lm.Message))
                {
                    OutLn($"{stateName}.TagArray[{--count}] = new(\"{{OriginalFormat}}\", {EscapeMessageString(lm.Message)});");
                }

                foreach (var p in lm.Parameters)
                {
                    if (NeedsASlot(p) && !p.HasDataClassification)
                    {
                        var key = $"\"{p.TagName}\"";
                        string value;

                        if (p.IsEnumerable)
                        {
                            value = p.PotentiallyNull
                                ? $"{p.ParameterNameWithAtIfNeeded} != null ? {LoggerMessageHelperType}.Stringify({p.ParameterNameWithAtIfNeeded}) : null"
                                : $"{LoggerMessageHelperType}.Stringify({p.ParameterNameWithAtIfNeeded})";
                        }
                        else
                        {
                            value = ShouldStringifyParameter(p)
                                ? ConvertParameterToString(p, p.ParameterNameWithAtIfNeeded)
                                : p.ParameterNameWithAtIfNeeded;
                        }

                        OutLn($"{stateName}.TagArray[{--count}] = new({key}, {value});");
                    }
                }

                foreach (var p in lm.Parameters)
                {
                    if (p.HasProperties && !p.SkipNullProperties)
                    {
                        p.TraverseParameterPropertiesTransitively((propertyChain, member) =>
                        {
                            if (!member.HasDataClassification)
                            {
                                var propName = PropertyChainToString(propertyChain, member, ".", omitReferenceName: p.OmitReferenceName);
                                var accessExpression = PropertyChainToString(propertyChain, member, "?.", nonNullSeparator: ".");

                                var ts = ShouldStringifyProperty(member)
                                    ? ConvertPropertyToString(member, accessExpression)
                                    : accessExpression;

                                var value = member.IsEnumerable
                                    ? $"{LoggerMessageHelperType}.Stringify({accessExpression})"
                                    : ts;

                                OutLn($"{stateName}.TagArray[{--count}] = new(\"{propName}\", {value});");
                            }
                        });
                    }
                }
            }

            if (numReservedClassifiedTags > 0)
            {
                OutLn();
                OutLn($"_ = {stateName}.ReserveClassifiedTagSpace({numReservedClassifiedTags});");
                int count = numReservedClassifiedTags;
                foreach (var p in lm.Parameters)
                {
                    if (NeedsASlot(p) && p.HasDataClassification)
                    {
                        var key = $"\"{p.TagName}\"";
                        var classification = _classificationMap[p.ClassificationAttributeTypes];

                        var value = ShouldStringifyParameter(p)
                            ? ConvertParameterToString(p, p.ParameterNameWithAtIfNeeded)
                            : p.ParameterNameWithAtIfNeeded;

                        OutLn($"{stateName}.ClassifiedTagArray[{--count}] = new({key}, {value}, {classification});");
                    }
                }

                foreach (var p in lm.Parameters)
                {
                    if (p.HasProperties && !p.SkipNullProperties)
                    {
                        p.TraverseParameterPropertiesTransitively((propertyChain, member) =>
                        {
                            if (member.HasDataClassification)
                            {
                                var propName = PropertyChainToString(propertyChain, member, ".", omitReferenceName: p.OmitReferenceName);
                                var accessExpression = PropertyChainToString(propertyChain, member, "?.", nonNullSeparator: ".");

                                var value = ShouldStringifyProperty(member)
                                    ? ConvertPropertyToString(member, accessExpression)
                                    : accessExpression;

                                var classification = _classificationMap[member.ClassificationAttributeTypes];

                                OutLn($"{stateName}.ClassifiedTagArray[{--count}] = new(\"{propName}\", {value}, {classification});");
                            }
                        });
                    }
                }
            }

            foreach (var p in lm.Parameters)
            {
                if (p.HasProperties && p.SkipNullProperties && !p.HasTagProvider)
                {
                    p.TraverseParameterPropertiesTransitively((propertyChain, member) =>
                    {
                        if (member.HasDataClassification)
                        {
                            var propName = PropertyChainToString(propertyChain, member, ".", omitReferenceName: p.OmitReferenceName);
                            var accessExpression = PropertyChainToString(propertyChain, member, "?.", nonNullSeparator: ".");

                            var value = ShouldStringifyProperty(member)
                                ? ConvertPropertyToString(member, accessExpression)
                                : accessExpression;

                            var classification = _classificationMap[member.ClassificationAttributeTypes];

                            if (member.PotentiallyNull)
                            {
                                OutOpenBrace();
                                OutLn($"var {tmpVarName} = {value};");
                                OutLn($"if ({tmpVarName} != null)");
                                OutOpenBrace();
                                OutLn($"{stateName}.AddClassifiedTag(\"{propName}\", {tmpVarName}, {classification});");
                                OutCloseBrace();
                                OutCloseBrace();
                                OutLn();
                            }
                            else
                            {
                                OutLn($"{stateName}.AddClassifiedTag(\"{propName}\", {value}, {classification});");
                            }
                        }
                        else
                        {
                            var propName = PropertyChainToString(propertyChain, member, ".", omitReferenceName: p.OmitReferenceName);
                            var accessExpression = PropertyChainToString(propertyChain, member, "?.", nonNullSeparator: ".");

                            var ts = ShouldStringifyProperty(member)
                                ? ConvertPropertyToString(member, accessExpression)
                                : accessExpression;

                            var value = member.IsEnumerable
                                ? $"{LoggerMessageHelperType}.Stringify({accessExpression})"
                                : ts;

                            if (member.PotentiallyNull)
                            {
                                if (member.IsEnumerable)
                                {
                                    OutOpenBrace();
                                    OutLn($"if ({accessExpression} != null)");
                                    OutOpenBrace();
                                    OutLn($"{stateName}.AddTag(\"{propName}\", {value});");
                                    OutCloseBrace();
                                    OutCloseBrace();
                                    OutLn();
                                }
                                else
                                {
                                    OutOpenBrace();
                                    OutLn($"var {tmpVarName} = {value};");
                                    OutLn($"if ({tmpVarName} != null)");
                                    OutOpenBrace();
                                    OutLn($"{stateName}.AddTag(\"{propName}\", {tmpVarName});");
                                    OutCloseBrace();
                                    OutCloseBrace();
                                    OutLn();
                                }
                            }
                            else
                            {
                                OutLn($"{stateName}.AddTag(\"{propName}\", {value});");
                            }
                        }
                    });
                }

                if (p.HasTagProvider)
                {
                    if (p.OmitReferenceName)
                    {
                        OutLn($"{stateName}.TagNamePrefix = string.Empty;");
                    }
                    else
                    {
                        OutLn($"{stateName}.TagNamePrefix = nameof({p.ParameterNameWithAtIfNeeded});");
                    }

                    OutLn($"{p.TagProvider!.ContainingType}.{p.TagProvider.MethodName}({stateName}, {p.ParameterNameWithAtIfNeeded});");
                }
            }
        }

        bool GenVariableAssignments(LoggingMethod lm, string lambdaStateName, int numReservedUnclassifiedTags, int numReservedClassifiedTags)
        {
            bool generatedAssignments = false;

            int index = numReservedUnclassifiedTags - 2;
            foreach (var p in lm.Parameters)
            {
                if (NeedsASlot(p) && !p.HasDataClassification)
                {
                    if (p.UsedAsTemplate)
                    {
                        var templates = lm.GetTemplatesForParameter(p);
                        foreach (var t in templates)
                        {
                            var atSign = p.NeedsAtSign ? "@" : string.Empty;
                            if (p.PotentiallyNull)
                            {
                                const string Null = "\"(null)\"";
                                OutLn($"var {atSign}{p.ParameterName} = {lambdaStateName}.TagArray[{index}].Value ?? {Null};");
                            }
                            else
                            {
                                OutLn($"var {atSign}{p.ParameterName} = {lambdaStateName}.TagArray[{index}].Value;");
                            }

                            generatedAssignments = true;
                        }
                    }

                    index--;
                }
            }

            index = numReservedClassifiedTags - 1;
            foreach (var p in lm.Parameters)
            {
                if (NeedsASlot(p) && p.HasDataClassification)
                {
                    if (p.UsedAsTemplate)
                    {
                        var templates = lm.GetTemplatesForParameter(p);
                        foreach (var t in templates)
                        {
                            var atSign = p.NeedsAtSign ? "@" : string.Empty;
                            if (p.PotentiallyNull)
                            {
                                const string Null = "\"(null)\"";
                                OutLn($"var {atSign}{p.ParameterName} = {lambdaStateName}.RedactedTagArray[{index}].Value ?? {Null};");
                            }
                            else
                            {
                                OutLn($"var {atSign}{p.ParameterName} = {lambdaStateName}.RedactedTagArray[{index}].Value;");
                            }

                            generatedAssignments = true;
                        }
                    }

                    index--;
                }
            }

            return generatedAssignments;
        }

        static (string name, bool isNullable) GetLogger(LoggingMethod lm)
        {
            string logger = lm.LoggerMember;
            bool isNullable = lm.LoggerMemberNullable;

            foreach (var p in lm.Parameters)
            {
                if (p.IsLogger)
                {
                    logger = p.ParameterName;
                    isNullable = p.IsNullable;
                    break;
                }
            }

            return (logger, isNullable);
        }
    }

    private void GenParameters(LoggingMethod lm)
    {
        OutEnumeration(lm.Parameters.Select(static p =>
        {
            if (p.Qualifier != null)
            {
                return $"{p.Qualifier} {p.Type} {p.ParameterNameWithAtIfNeeded}";
            }

            return $"{p.Type} {p.ParameterNameWithAtIfNeeded}";
        }));
    }

    private string PropertyChainToString(
        IEnumerable<LoggingProperty> propertyChain,
        LoggingProperty leafProperty,
        string separator,
        string? nonNullSeparator = null,
        bool omitReferenceName = false)
    {
        bool needAts = nonNullSeparator == ".";
        var adjustedNonNullSeparator = nonNullSeparator ?? separator;
        var localStringBuilder = _sbPool.GetStringBuilder();
        try
        {
            int count = 0;
            foreach (var property in propertyChain)
            {
                count++;
                if (omitReferenceName && count == 1)
                {
                    continue;
                }

                _ = localStringBuilder
                    .Append(needAts ? property.PropertyNameWithAt : property.PropertyName)
                    .Append(property.PotentiallyNull ? separator : adjustedNonNullSeparator);
            }

            // Last item:
            _ = localStringBuilder.Append(needAts ? leafProperty.PropertyNameWithAt : leafProperty.PropertyName);

            return localStringBuilder.ToString();
        }
        finally
        {
            _sbPool.ReturnStringBuilder(localStringBuilder);
        }
    }
}
