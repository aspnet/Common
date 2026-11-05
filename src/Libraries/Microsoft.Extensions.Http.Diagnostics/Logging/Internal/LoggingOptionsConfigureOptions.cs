// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Http.Logging.Internal;

/// <remarks>
/// This is a workaround for <see href="https://github.com/dotnet/runtime/issues/83599">dotnet/runtime#83599</see>
/// and can be removed when the configuration binding source generator supports custom conversion for
/// <see cref="DataClassification"/>.
/// </remarks>
internal sealed class LoggingOptionsConfigureOptions : IConfigureNamedOptions<LoggingOptions>
{
#pragma warning disable EXTEXP0002 // DataClassificationTypeConverter is experimental.
    private static readonly DataClassificationTypeConverter _dataClassificationConverter = new();
#pragma warning restore EXTEXP0002

    private readonly string? _name;
    private readonly IConfigurationSection _section;

    public LoggingOptionsConfigureOptions(string? name, IConfigurationSection section)
    {
        _name = name;
        _section = Throw.IfNull(section);
    }

    void IConfigureOptions<LoggingOptions>.Configure(LoggingOptions options) => Configure(Options.Options.DefaultName, options);

    public void Configure(string? name, LoggingOptions options)
    {
        if (!string.Equals(name, _name, StringComparison.Ordinal) || !_section.Exists())
        {
            return;
        }

        BindValue(_section, nameof(LoggingOptions.LogRequestStart), bool.Parse, value => options.LogRequestStart = value);
        BindDataClassifications(_section.GetSection(nameof(LoggingOptions.RequestQueryParametersDataClasses)), options.RequestQueryParametersDataClasses);
        BindValue(_section, nameof(LoggingOptions.LogBody), bool.Parse, value => options.LogBody = value);
        BindValue(
            _section,
            nameof(LoggingOptions.BodySizeLimit),
            static value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            value => options.BodySizeLimit = value);
        BindValue(
            _section,
            nameof(LoggingOptions.BodyReadTimeout),
            static value => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
            value => options.BodyReadTimeout = value);
        BindSet(_section.GetSection(nameof(LoggingOptions.RequestBodyContentTypes)), options.RequestBodyContentTypes);
        BindSet(_section.GetSection(nameof(LoggingOptions.ResponseBodyContentTypes)), options.ResponseBodyContentTypes);
        BindDataClassifications(_section.GetSection(nameof(LoggingOptions.RequestHeadersDataClasses)), options.RequestHeadersDataClasses);
        BindDataClassifications(_section.GetSection(nameof(LoggingOptions.ResponseHeadersDataClasses)), options.ResponseHeadersDataClasses);
        BindEnum<OutgoingPathLoggingMode>(_section, nameof(LoggingOptions.RequestPathLoggingMode), value => options.RequestPathLoggingMode = value);
        BindEnum<HttpRouteParameterRedactionMode>(_section, nameof(LoggingOptions.RequestPathParameterRedactionMode), value => options.RequestPathParameterRedactionMode = value);
        BindDataClassifications(_section.GetSection(nameof(LoggingOptions.RouteParameterDataClasses)), options.RouteParameterDataClasses);
        BindValue(_section, nameof(LoggingOptions.LogContentHeaders), bool.Parse, value => options.LogContentHeaders = value);
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
