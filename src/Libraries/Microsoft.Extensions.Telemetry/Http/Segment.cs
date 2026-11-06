// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Extensions.Http.Diagnostics;

/// <summary>
/// Struct to hold the metadata about a route's segment.
/// </summary>
#pragma warning disable CA1815 // Override equals and operator equals on value types
internal readonly struct Segment
#pragma warning restore CA1815 // Override equals and operator equals on value types
{
    private const string ControllerParameter = "controller";
    private const string ActionParameter = "action";

    /// <summary>
    /// Initializes a new instance of the <see cref="Segment"/> struct.
    /// </summary>
    /// <param name="start">Start index of the segment.</param>
    /// <param name="end">End index of the segment.</param>
    /// <param name="content">Actual content of the segment.</param>
    /// <param name="isParam">If the segment is a param.</param>
    /// <param name="paramName">Name of the parameter.</param>
    /// <param name="defaultValue">Default value of the parameter.</param>
    /// <param name="isCatchAll">If the segment is a catch-all parameter.</param>
    public Segment(
        int start, int end, string content, bool isParam,
        string paramName = "", string defaultValue = "", bool isCatchAll = false)
    {
        Start = start;
        End = end;
        Content = content;
        IsParam = isParam;
        ParamName = paramName;
        DefaultValue = defaultValue;
        IsCatchAll = isCatchAll;
    }

    /// <summary>
    /// Gets start index of the segment in the escaped path, in which an escaped brace "{{" or "}}" of a literal is "%7B" or "%7D".
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets end index of the segment in the escaped path, in which an escaped brace "{{" or "}}" of a literal is "%7B" or "%7D".
    /// </summary>
    public int End { get; }

    /// <summary>
    /// Gets content of the segment.
    /// </summary>
    public string Content { get; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the segment is a parameter.
    /// </summary>
    public bool IsParam { get; }

    /// <summary>
    /// Gets a name of the parameter.
    /// </summary>
    public string ParamName { get; } = string.Empty;

    /// <summary>
    /// Gets a default value of the parameter.
    /// </summary>
    public string DefaultValue { get; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the segment is a catch-all parameter.
    /// </summary>
    public bool IsCatchAll { get; }

    internal static bool IsKnownUnredactableParameter(string parameter) =>
        parameter.Equals(ControllerParameter, StringComparison.OrdinalIgnoreCase) ||
        parameter.Equals(ActionParameter, StringComparison.OrdinalIgnoreCase);
}
