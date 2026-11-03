// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

extern alias Reporting;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using PathValidation = Reporting::Microsoft.Extensions.AI.Evaluation.Reporting.Utilities.PathValidation;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

public class PathValidationTests
{
    // ──────────────────────────────────────────────
    //  ValidatePathSegment – valid inputs
    // ──────────────────────────────────────────────

    [Fact]
    public void ValidatePathSegment_Null_DoesNotThrow()
    {
        PathValidation.ValidatePathSegment(null, "param");
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("My Scenario")]
    [InlineData("run-2024-01-01")]
    [InlineData("iteration_0")]
    [InlineData("a")]
    [InlineData("...")]
    [InlineData("..x")]
    [InlineData("x..")]
    public void ValidatePathSegment_ValidNames_DoesNotThrow(string segment)
    {
        PathValidation.ValidatePathSegment(segment, "param");
    }

    // ──────────────────────────────────────────────
    //  ValidatePathSegment – invalid inputs
    // ──────────────────────────────────────────────

    [Fact]
    public void ValidatePathSegment_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PathValidation.ValidatePathSegment("", "param"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    public void ValidatePathSegment_TraversalLiterals_Throws(string segment)
    {
        Assert.Throws<ArgumentException>(() =>
            PathValidation.ValidatePathSegment(segment, "param"));
    }

    [Theory]
    [InlineData("foo/bar")]
    [InlineData("../secret")]
    public void ValidatePathSegment_ContainsForwardSlash_Throws(string segment)
    {
        Assert.Throws<ArgumentException>(() =>
            PathValidation.ValidatePathSegment(segment, "param"));
    }

    [Theory]
    [InlineData("foo\\bar")]
    [InlineData("..\\secret")]
    public void ValidatePathSegment_ContainsBackslash_ThrowsOnWindows(string segment)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Backslash is a path separator (and invalid filename char) on Windows.
            Assert.Throws<ArgumentException>(() =>
                PathValidation.ValidatePathSegment(segment, "param"));
        }
        else
        {
            // Backslash is a valid filename character on Linux/macOS.
            PathValidation.ValidatePathSegment(segment, "param");
        }
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData(" both ")]
    public void ValidatePathSegment_WhitespacePadded_Throws(string segment)
    {
        Assert.Throws<ArgumentException>(() =>
            PathValidation.ValidatePathSegment(segment, "param"));
    }

    [Fact]
    public void ValidatePathSegment_NullCharacter_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PathValidation.ValidatePathSegment("foo\0bar", "param"));
    }

    // ──────────────────────────────────────────────
    //  EnsureWithinRoot – paths inside root
    // ──────────────────────────────────────────────

    [Fact]
    public void EnsureWithinRoot_ChildPath_ReturnsResolvedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot");
        string child = Path.Combine(root, "sub", "file.txt");

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void EnsureWithinRoot_DeeplyNested_ReturnsResolvedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot");
        string child = Path.Combine(root, "a", "b", "c", "d.json");

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void EnsureWithinRoot_RootWithTrailingSeparator_Works()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot") + Path.DirectorySeparatorChar;
        string child = Path.Combine(root, "file.txt");

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    // ──────────────────────────────────────────────
    //  EnsureWithinRoot – paths escaping root
    // ──────────────────────────────────────────────

    [Fact]
    public void EnsureWithinRoot_DotDotEscapes_Throws()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot");
        string escaped = Path.Combine(root, "..", "outside");

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, escaped));
    }

    [Fact]
    public void EnsureWithinRoot_MultipleDotDots_Throws()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot", "nested");
        string escaped = Path.Combine(root, "..", "..", "outside");

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, escaped));
    }

    [Fact]
    public void EnsureWithinRoot_CompletelyDifferentPath_Throws()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot");
        string other = Path.Combine(Path.GetTempPath(), "other", "file.txt");

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, other));
    }

    [Fact]
    public void EnsureWithinRoot_SiblingWithPrefix_Throws()
    {
        // Verifies that "testroot-sibling" is NOT treated as being inside "testroot".
        string root = Path.Combine(Path.GetTempPath(), "testroot");
        string sibling = Path.Combine(Path.GetTempPath(), "testroot-sibling", "file.txt");

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, sibling));
    }

    [Fact]
    public void EnsureWithinRoot_PathEqualsRoot_DoesNotThrow()
    {
        string root = Path.Combine(Path.GetTempPath(), "testroot");

        string result = PathValidation.EnsureWithinRoot(root, root);

        Assert.Equal(Path.GetFullPath(root), result);
    }

    // ──────────────────────────────────────────────
    //  Integration: DiskBasedResultStore rejects traversal
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DiskBasedResultStore_DeleteWithTraversal_Throws()
    {
        string storagePath = Path.Combine(Path.GetTempPath(), "M.E.AI.Eval.PathTests", Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(storagePath);
            var store = new Storage.DiskBasedResultStore(storagePath);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.DeleteResultsAsync(executionName: "..", scenarioName: "../sentinel").AsTask());
        }
        finally
        {
            try
            {
                Directory.Delete(storagePath, true);
            }
#pragma warning disable CA1031 // Do not catch general exception types.
            catch
#pragma warning restore CA1031
            {
                // Best effort cleanup.
            }
        }
    }

    [Fact]
    public async Task DiskBasedResponseCacheProvider_TraversalInScenarioName_Throws()
    {
        string storagePath = Path.Combine(Path.GetTempPath(), "M.E.AI.Eval.PathTests", Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(storagePath);
            var provider = new Storage.DiskBasedResponseCacheProvider(storagePath);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                provider.GetCacheAsync("..", "..").AsTask());
        }
        finally
        {
            try
            {
                Directory.Delete(storagePath, true);
            }
#pragma warning disable CA1031 // Do not catch general exception types.
            catch
#pragma warning restore CA1031
            {
                // Best effort cleanup.
            }
        }
    }

    // ──────────────────────────────────────────────
    //  EnsureWithinRoot – UNC paths (Windows only)
    // ──────────────────────────────────────────────

    [Fact]
    public void EnsureWithinRoot_UncPath_ChildPath_ReturnsResolved()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"\\server\share\data";
        string child = @"\\server\share\data\sub\file.txt";

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void EnsureWithinRoot_UncPath_DifferentShare_Throws()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"\\server\share\data";
        string other = @"\\server\share\other\file.txt";

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, other));
    }

    [Fact]
    public void EnsureWithinRoot_UncPath_DotDotEscapes_Throws()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"\\server\share\data";
        string escaped = @"\\server\share\data\..\other";

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, escaped));
    }

    [Fact]
    public void EnsureWithinRoot_UncPath_SiblingWithPrefix_Throws()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"\\server\share\data";
        string sibling = @"\\server\share\data-sibling\file.txt";

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, sibling));
    }

    [Fact]
    public void EnsureWithinRoot_UncPath_PathEqualsRoot_DoesNotThrow()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"\\server\share\data";

        string result = PathValidation.EnsureWithinRoot(root, root);

        Assert.Equal(Path.GetFullPath(root), result);
    }

    // ──────────────────────────────────────────────
    //  EnsureWithinRoot – short (8.3) Windows paths
    // ──────────────────────────────────────────────

    [Fact]
    public void EnsureWithinRoot_ShortPathRoot_LongPathChild_DocumentedBehavior()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        // Short (8.3) paths are NOT consistently normalized by Path.GetFullPath
        // across .NET versions. This test documents that if the root uses a short
        // path form and the child uses the long form, the behavior depends on
        // whether GetFullPath expands 8.3 names on the current runtime.
        // This is acceptable because callers always construct paths relative
        // to the same root string via Path.Combine.
        string longRoot = Path.Combine(Path.GetTempPath(), "LongDirectoryName_ForTesting");
        Directory.CreateDirectory(longRoot);
        try
        {
            string shortRoot = GetShortPath(longRoot);
            if (string.Equals(shortRoot, longRoot, StringComparison.OrdinalIgnoreCase))
            {
                // 8.3 names not enabled on this volume — skip silently.
                return;
            }

            string longChild = Path.Combine(longRoot, "file.txt");

            // Check whether this runtime expands 8.3 names in GetFullPath.
            bool runtimeExpands8Dot3 = string.Equals(
                Path.GetFullPath(shortRoot),
                Path.GetFullPath(longRoot),
                StringComparison.OrdinalIgnoreCase);

            if (runtimeExpands8Dot3)
            {
                // GetFullPath normalizes both to long form — mixed usage works.
                string result = PathValidation.EnsureWithinRoot(shortRoot, longChild);
                Assert.Equal(Path.GetFullPath(longChild), result);
            }
            else
            {
                // GetFullPath preserves 8.3 — mixed representations don't match.
                Assert.Throws<InvalidOperationException>(() =>
                    PathValidation.EnsureWithinRoot(shortRoot, longChild));
            }
        }
        finally
        {
            Directory.Delete(longRoot, true);
        }
    }

    [Fact]
    public void EnsureWithinRoot_ConsistentShortPaths_Works()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        // When both root and child are constructed from the same short-path
        // string via Path.Combine, EnsureWithinRoot succeeds because
        // Path.GetFullPath treats both consistently.
        // Note: the child file must exist on disk because some runtimes
        // (e.g. .NET Framework) expand 8.3 names only for existing paths.
        string longRoot = Path.Combine(Path.GetTempPath(), "LongDirectoryName_ForTesting");
        Directory.CreateDirectory(longRoot);
        try
        {
            string shortRoot = GetShortPath(longRoot);
            if (string.Equals(shortRoot, longRoot, StringComparison.OrdinalIgnoreCase))
            {
                // 8.3 names not enabled on this volume — skip silently.
                return;
            }

            // Create the child file so GetFullPath expands consistently.
            string child = Path.Combine(shortRoot, "file.txt");
            File.WriteAllText(Path.Combine(longRoot, "file.txt"), string.Empty);

            string result = PathValidation.EnsureWithinRoot(shortRoot, child);

            Assert.Equal(Path.GetFullPath(child), result);
        }
        finally
        {
            Directory.Delete(longRoot, true);
        }
    }

    // ──────────────────────────────────────────────
    //  EnsureWithinRoot – additional edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void EnsureWithinRoot_AltSeparatorInRoot_Works()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        // Forward slash is an alternate directory separator on Windows.
        string root = Path.GetTempPath().Replace('\\', '/') + "testroot";
        string child = Path.Combine(root, "sub", "file.txt");

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void EnsureWithinRoot_CaseMismatch_BehavesPerPlatform()
    {
        string root = Path.Combine(Path.GetTempPath(), "TestRoot");
        string child = Path.Combine(Path.GetTempPath(), "testroot", "file.txt");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows is case-insensitive: should succeed.
            string result = PathValidation.EnsureWithinRoot(root, child);
            Assert.Equal(Path.GetFullPath(child), result);
        }
        else
        {
            // Linux/macOS is case-sensitive: should throw.
            Assert.Throws<InvalidOperationException>(() =>
                PathValidation.EnsureWithinRoot(root, child));
        }
    }

    [Fact]
    public void EnsureWithinRoot_DriveRoot_ChildPath_Works()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"C:\";
        string child = @"C:\some\nested\file.txt";

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void EnsureWithinRoot_DriveRoot_DifferentDrive_Throws()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Linux/macOS");

        string root = @"C:\data";
        string other = @"D:\data\file.txt";

        Assert.Throws<InvalidOperationException>(() =>
            PathValidation.EnsureWithinRoot(root, other));
    }

    [Fact]
    public void EnsureWithinRoot_UnixAbsoluteRoot_ChildPath_Works()
    {
        Assert.SkipUnless(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Windows");

        string root = "/tmp/testroot";
        string child = "/tmp/testroot/sub/file.txt";

        string result = PathValidation.EnsureWithinRoot(root, child);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);
    }

    private static string GetShortPath(string longPath)
    {
        var buffer = new StringBuilder(260);
        uint len = NativeMethods.GetShortPathNameW(longPath, buffer, (uint)buffer.Capacity);
        return len > 0 ? buffer.ToString() : longPath;
    }
}
