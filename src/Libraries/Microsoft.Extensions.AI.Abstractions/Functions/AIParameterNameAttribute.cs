// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Specifies the schema name to use for an <see cref="AIFunction"/> parameter.</summary>
/// <remarks>
/// The name is the identifier a model sees and uses when supplying an argument to a function.
/// By default this is inferred from .NET metadata. Apply this attribute to use a different identifier.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
[Experimental(DiagnosticIds.Experiments.AIFunctionAndParameterName, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class AIParameterNameAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="AIParameterNameAttribute"/> class.</summary>
    /// <param name="name">The schema name to use for the parameter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or composed entirely of whitespace.</exception>
    public AIParameterNameAttribute(string name)
    {
        Name = Throw.IfNullOrWhitespace(name);
    }

    /// <summary>Gets the schema name to use for the parameter.</summary>
    public string Name { get; }
}
