// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.VectorData.ProviderServices;

/// <summary>
/// Contains options affecting model building; passed to <see cref="CollectionModelBuilder"/>.
/// This is an internal support type meant for use by providers only and not by applications.
/// </summary>
[Experimental(DiagnosticIds.Experiments.VectorDataProviderServices, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class CollectionModelBuildingOptions
{
    /// <summary>
    /// Gets a value indicating whether multiple vector properties are supported.
    /// </summary>
    public required bool SupportsMultipleVectors { get; init; }

    /// <summary>
    /// Gets a value indicating whether at least one vector property is required.
    /// </summary>
    public required bool RequiresAtLeastOneVector { get; init; }

    /// <summary>
    /// Gets a value indicating whether an external serializer will be used (for example, System.Text.Json).
    /// </summary>
    public bool UsesExternalSerializer { get; init; }

    /// <summary>
    /// Gets or initializes the special, reserved name for the key property of the database.
    /// </summary>
    /// <remarks>
    /// When set, the model builder manages the key storage name, and users cannot customize it.
    /// </remarks>
    public string? ReservedKeyStorageName { get; init; }
}
