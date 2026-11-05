// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Extensions.ServiceDiscovery;

/// <summary>
/// Options for service endpoint resolution.
/// </summary>
public sealed class ServiceDiscoveryOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether all URI schemes for URIs resolved by the service discovery system are allowed.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to allow all URI schemes; <see langword="false"/> to allow only schemes specified in <see cref="AllowedSchemes"/>.
    /// </value>
    public bool AllowAllSchemes { get; set; } = true;

    /// <summary>
    /// Gets or sets the period between polling attempts for providers which do not support refresh notifications via <see cref="IChangeToken.ActiveChangeCallbacks"/>.
    /// </summary>
    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the collection of allowed URI schemes for URIs resolved by the service discovery system when multiple schemes are specified, for example "https+http://_endpoint.service".
    /// </summary>
    /// <remarks>
    /// When <see cref="AllowAllSchemes"/> is <see langword="true"/>, this property is ignored.
    /// </remarks>
    public IList<string> AllowedSchemes { get; set; } = new List<string>();

    /// <summary>
    /// Filters the specified URI schemes to include only those that are applicable, based on the current settings.
    /// </summary>
    /// <param name="requestedSchemes">The URI schemes to be evaluated against the allowed schemes.</param>
    /// <returns>
    /// The URI schemes that are applicable. If no schemes are requested, all allowed schemes are returned.
    /// If all schemes are allowed, only the requested schemes are returned.
    /// Otherwise, the intersection of requested and allowed schemes is returned.
    /// </returns>
    public IReadOnlyList<string> ApplyAllowedSchemes(IReadOnlyList<string> requestedSchemes)
    {
        ArgumentNullException.ThrowIfNull(requestedSchemes);

        if (requestedSchemes.Count > 0)
        {
            if (AllowAllSchemes)
            {
                return requestedSchemes;
            }

            List<string> result = [];
            foreach (var s in requestedSchemes)
            {
                foreach (var allowed in AllowedSchemes)
                {
                    if (string.Equals(s, allowed, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(s);
                        break;
                    }
                }
            }

            return result.AsReadOnly();
        }

        // If no schemes were specified, but a set of allowed schemes were specified, allow those.
        return new ReadOnlyCollection<string>(AllowedSchemes);
    }
}
