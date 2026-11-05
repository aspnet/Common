// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.ServiceDiscovery.Configuration;

namespace Microsoft.Extensions.ServiceDiscovery;

/// <summary>
/// Options for <see cref="ConfigurationServiceEndpointProvider"/>.
/// </summary>
public sealed class ConfigurationServiceEndpointProviderOptions
{
    /// <summary>
    /// Gets or sets the name of the configuration section that contains service endpoints.
    /// </summary>
    /// <value>
    /// The default value is <c>"Services"</c>.
    /// </value>
    public string SectionName { get; set; } = "Services";

    /// <summary>
    /// Gets or sets a delegate used to determine whether to apply host name metadata to each resolved endpoint.
    /// </summary>
    /// <value>
    /// The default delegate returns <see langword="false"/>.
    /// </value>
    public Func<ServiceEndpoint, bool> ShouldApplyHostNameMetadata { get; set; } = _ => false;
}
