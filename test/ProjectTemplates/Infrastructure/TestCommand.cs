// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Shared.ProjectTemplates.Tests;

public abstract class TestCommand
{
    public string? FileName { get; set; }

    public string? WorkingDirectory { get; set; }

    public TimeSpan? Timeout { get; set; }

    public List<string> Arguments { get; } = [];

    public Dictionary<string, string> EnvironmentVariables = [];

    public virtual async Task<TestCommandResult> ExecuteAsync(ITestOutputHelper outputHelper)
    {
        if (string.IsNullOrEmpty(FileName))
        {
            throw new InvalidOperationException($"The {nameof(TestCommand)} did not specify an executable file name.");
        }

        var processStartInfo = new ProcessStartInfo(FileName, Arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        if (WorkingDirectory is not null)
        {
            processStartInfo.WorkingDirectory = WorkingDirectory;
        }

        foreach (var (key, value) in EnvironmentVariables)
        {
            processStartInfo.EnvironmentVariables[key] = value;
        }

        var exitedTcs = new TaskCompletionSource();
        var standardOutputBuilder = new StringBuilder();
        var standardErrorBuilder = new StringBuilder();

        using var process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += MakeOnDataReceivedHandler(standardOutputBuilder);
        process.ErrorDataReceived += MakeOnDataReceivedHandler(standardErrorBuilder);
        process.Exited += (sender, args) =>
        {
            exitedTcs.SetResult();
        };

        DataReceivedEventHandler MakeOnDataReceivedHandler(StringBuilder outputBuilder) => (sender, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (outputBuilder)
            {
                outputBuilder.AppendLine(args.Data);
            }

            lock (outputHelper)
            {
                outputHelper.WriteLine(args.Data);
            }
        };

        outputHelper.WriteLine($"Executing '{processStartInfo.FileName} {string.Join(" ", Arguments)}' in working directory '{processStartInfo.WorkingDirectory}'");

        using var timeoutCts = new CancellationTokenSource();
        if (Timeout is { } timeout)
        {
            timeoutCts.CancelAfter(timeout);
        }

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await exitedTcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var elapsedTime = Stopwatch.GetElapsedTime(startTimestamp);
            outputHelper.WriteLine($"Process ran for {elapsedTime} seconds.");

            return new(standardOutputBuilder, standardErrorBuilder, process.ExitCode);
        }
        catch (Exception ex)
        {
            outputHelper.WriteLine($"An exception occurred: {ex}");
            throw;
        }
        finally
        {
            if (!process.TryGetHasExited())
            {
                var elapsedTime = Stopwatch.GetElapsedTime(startTimestamp);
                outputHelper.WriteLine($"The process has been running for {elapsedTime} seconds. Terminating the process.");
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
