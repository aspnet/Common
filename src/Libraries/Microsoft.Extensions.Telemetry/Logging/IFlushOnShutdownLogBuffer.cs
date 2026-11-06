// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Marks a log buffer whose retained records must be emitted before logging providers are disposed.
/// </summary>
internal interface IFlushOnShutdownLogBuffer
{
}
#endif
