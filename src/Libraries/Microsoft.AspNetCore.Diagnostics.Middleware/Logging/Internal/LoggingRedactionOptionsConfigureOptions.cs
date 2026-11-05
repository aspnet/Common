// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.AspNetCore.Diagnostics.Logging.Internal;

/// <remarks>
/// This is a workaround for <see href="https://github.com/dotnet/runtime/issues/83599">dotnet/runtime#83599</see>
/// and can be removed when the configuration binding source generator supports custom conversion for
/// <see cref="DataClassification"/>.
/// </remarks>
internal sealed class LoggingRedactionOptionsConfigureOptions : IConfigureOptions<LoggingRedactionOptions>
{
#pragma warning disable EXTEXP0002 // DataClassificationTypeConverter is experimental.
    private static readonly DataClassificationTypeConverter _dataClassificationConverter = new();
#pragma warning restore EXTEXP0002

    private readonly IConfigurationSection _section;

    public LoggingRedactionOptionsConfigureOptions(IConfigurationSection section)
    {
        _section = Throw.IfNull(section);
    }

    public void Configure(LoggingRedactionOptions options)
    {
        if (!_section.Exists())
        {
            return;
        }

        BindEnum<IncomingPathLoggingMode>(_section, nameof(LoggingRedactionOptions.RequestPathLoggingMode), value => options.RequestPathLoggingMode = value);
        BindEnum<HttpRouteParameterRedactionMode>(_section, nameof(LoggingRedactionOptions.RequestPathParameterRedactionMode), value => options.RequestPathParameterRedactionMode = value);
        BindDataClassifications(_section.GetSection(nameof(LoggingRedactionOptions.RouteParameterDataClasses)), options.RouteParameterDataClasses);
        BindDataClassifications(_section.GetSection(nameof(LoggingRedactionOptions.RequestHeadersDataClasses)), options.RequestHeadersDataClasses);
        BindDataClassifications(_section.GetSection(nameof(LoggingRedactionOptions.ResponseHeadersDataClasses)), options.ResponseHeadersDataClasses);
        BindSet(_section.GetSection(nameof(LoggingRedactionOptions.ExcludePathStartsWith)), options.ExcludePathStartsWith);
        BindValue(_section, nameof(LoggingRedactionOptions.IncludeUnmatchedRoutes), bool.Parse, value => options.IncludeUnmatchedRoutes = value);
    }

    private static void BindSet(IConfigurationSection section, ISet<string> destination)
    {
        foreach (IConfigurationSection child in section.GetChildren())
        {
            if (child.Value is string value)
            {
                _ = destination.Add(value);
            }
        }
    }

    private static void BindDataClassifications(IConfigurationSection section, IDictionary<string, DataClassification> destination)
    {
        foreach (IConfigurationSection child in section.GetChildren())
        {
            destination[child.Key] = ParseDataClassification(child);
        }
    }

    private static DataClassification ParseDataClassification(IConfigurationSection section)
    {
        // A classification is either a string ("None", "Unknown" or "Taxonomy:Value") or a { TaxonomyName, Value } object.
        try
        {
#pragma warning disable EXTEXP0002 // DataClassificationTypeConverter is experimental.
            if (section.Value is string value)
            {
                return (DataClassification)_dataClassificationConverter.ConvertFromInvariantString(value)!;
            }
#pragma warning restore EXTEXP0002

            return new DataClassification(
                section[nameof(DataClassification.TaxonomyName)]!,
                section[nameof(DataClassification.Value)]!);
        }
        catch (Exception exception)
        {
            throw CreateBindingException(section.Path, typeof(DataClassification), exception);
        }
    }

    private static void BindEnum<TEnum>(IConfigurationSection section, string key, Action<TEnum> setter)
        where TEnum : struct
        => BindValue(section, key, ParseEnum<TEnum>, setter);

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct
        => Enum.TryParse(value, ignoreCase: true, out TEnum result)
            ? result
            : throw new FormatException($"'{value}' is not a valid value for {typeof(TEnum)}.");

    private static void BindValue<T>(IConfigurationSection section, string key, Func<string, T> parser, Action<T> setter)
    {
        IConfigurationSection valueSection = section.GetSection(key);
        if (valueSection.Value is not string value)
        {
            return;
        }

        try
        {
            setter(parser(value));
        }
        catch (Exception exception)
        {
            throw CreateBindingException(valueSection.Path, typeof(T), exception);
        }
    }

    // Only the path is reported, since a configuration value may be a secret.
    private static InvalidOperationException CreateBindingException(string path, Type type, Exception innerException)
        => new($"Failed to convert configuration value at '{path}' to type '{type}'.", innerException);
}

#endif
