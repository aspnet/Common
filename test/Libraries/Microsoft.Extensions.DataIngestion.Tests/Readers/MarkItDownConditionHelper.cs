// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

/// <summary>
/// Checks whether MarkItDown is installed and accessible. Used to conditionally skip tests.
/// </summary>
internal static class MarkItDownConditionHelper
{
    internal static readonly Lazy<bool> IsInstalled = new(CanInvokeMarkItDown);

    private static bool CanInvokeMarkItDown()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "markitdown",
            Arguments = "--help",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return false;
        }

        while (process.StandardOutput.Peek() >= 0)
        {
            _ = process.StandardOutput.ReadLine();
        }

        process.WaitForExit();

        return true;
    }
}
