// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Microsoft.Extensions.Telemetry.Bench;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--retained-memory", StringComparison.Ordinal))
        {
            RetainedMemoryMeasurement.Run();
            return;
        }

        if (args.Length == 3 && string.Equals(args[0], "--retained-memory-worker", StringComparison.Ordinal))
        {
            RetainedMemoryMeasurement.RunWorker(args[1], args[2]);
            return;
        }

        if (args.Length == 1 && string.Equals(args[0], "--serialized-exporter-volume", StringComparison.Ordinal))
        {
            SerializedExporterVolumeMeasurement.Run();
            return;
        }

        if (args.Length == 1 && string.Equals(args[0], "--sustained-gc", StringComparison.Ordinal))
        {
            SustainedGcPressureMeasurement.Run();
            return;
        }

        if (args.Length == 2 && string.Equals(args[0], "--sustained-gc", StringComparison.Ordinal))
        {
            SustainedGcPressureMeasurement.Run(args[1]);
            return;
        }

        if (args.Length == 3 && string.Equals(args[0], "--sustained-gc", StringComparison.Ordinal))
        {
            SustainedGcPressureMeasurement.Run(args[1], args[2]);
            return;
        }

        if (args.Length == 5 && string.Equals(args[0], "--sustained-gc-worker", StringComparison.Ordinal))
        {
            SustainedGcPressureMeasurement.RunWorker(args[1], args[2], args[3], args[4]);
            return;
        }

        var dontRequireSlnToRunBenchmarks = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(Job.MediumRun.WithEnvironmentVariable("DOTNET_TieredCompilation", "0"));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, dontRequireSlnToRunBenchmarks);
    }
}
