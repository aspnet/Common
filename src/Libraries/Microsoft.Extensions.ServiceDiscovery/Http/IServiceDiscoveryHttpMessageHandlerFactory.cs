// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.ServiceDiscovery.Http;

/// <summary>
/// Represents a factory that creates <see cref="HttpMessageHandler"/> instances that resolve endpoints using service discovery
/// before delegating to a provided handler.
/// </summary>
public interface IServiceDiscoveryHttpMessageHandlerFactory
{
    /// <summary>
    /// Creates an <see cref="HttpMessageHandler"/> instance that resolves endpoints using service discovery before
    /// delegating to a provided handler.
    /// </summary>
    /// <param name="handler">The handler to delegate to.</param>
    /// <returns>The new <see cref="HttpMessageHandler"/>.</returns>
    HttpMessageHandler CreateHandler(HttpMessageHandler handler);
}
