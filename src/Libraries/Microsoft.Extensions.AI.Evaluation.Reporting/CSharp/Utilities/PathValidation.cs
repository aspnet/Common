// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Shared.Diagnostics;

#if NET
using System.Runtime.InteropServices;
#endif

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Utilities;

internal static class PathValidation
{
    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

#pragma warning disable CA1802 // Use literals where appropriate
    private static readonly StringComparison _pathComparison =
#if NET
        // Windows paths are case-insensitive; Linux/macOS paths are case-sensitive.
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
#else
        StringComparison.OrdinalIgnoreCase; // .NET Framework and .NET Standard only run on Windows
#endif
#pragma warning restore CA1802 // Use literals where appropriate

    /// <summary>
    /// Validates that a path segment is a safe single directory or file name.
    /// Throws <see cref="ArgumentException"/> if the segment contains path separators,
    /// invalid file name characters, or directory traversal sequences.
    /// </summary>
    internal static void ValidatePathSegment(string? segment, string paramName)
    {
        if (segment is null)
        {
            return;
        }

        if (segment.Length == 0
            || segment != segment.Trim()
            || segment is "."
            || segment is ".."
            || segment.IndexOfAny(_invalidFileNameChars) >= 0)
        {
            Throw.ArgumentException(
                paramName,
                $"The parameter '{paramName}' contains invalid path characters or directory traversal sequences.");
        }
    }

    /// <summary>
    /// Verifies that a fully resolved path is contained within the specified root directory.
    /// Both paths are canonicalized via <see cref="Path.GetFullPath(string)"/> before comparison.
    /// Throws <see cref="InvalidOperationException"/> if the resolved path escapes the root.
    /// </summary>
    internal static string EnsureWithinRoot(string rootPath, string resolvedPath)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        string normalizedRoot = fullRoot;
        string fullResolved = Path.GetFullPath(resolvedPath);

        // Ensure the root ends with a directory separator so that a root of
        // "/foo/bar" does not match a path like "/foo/bar-sibling/file".
#if NET11_0_OR_GREATER
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
#elif NET
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar) &&
            !normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar))
#else
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
            !normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
#endif
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        if (!fullResolved.StartsWith(normalizedRoot, _pathComparison) &&
            !string.Equals(fullRoot, fullResolved, _pathComparison))
        {
            throw new InvalidOperationException(
                "The resolved path escapes the configured root directory. " +
                "This may indicate a path traversal attempt.");
        }

        return fullResolved;
    }
}
