// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI.Evaluation.Reporting.JsonSerialization;
using Microsoft.Extensions.AI.Evaluation.Reporting.Utilities;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Storage;

/// <summary>
/// An <see cref="IEvaluationResultStore"/> implementation that stores <see cref="ScenarioRunResult"/>s on disk.
/// </summary>
public sealed class DiskBasedResultStore : IEvaluationResultStore
{
    private const string DeserializationFailedMessage = "Unable to deserialize the scenario run result file at {0}.";

#if NET
    private static EnumerationOptions InTopDirectoryOnly { get; } =
        new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };
#else
    private const SearchOption InTopDirectoryOnly = SearchOption.TopDirectoryOnly;
#endif

    private readonly string _resultsRootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskBasedResultStore"/> class.
    /// </summary>
    /// <param name="storageRootPath">
    /// The path to a directory on disk under which the <see cref="ScenarioRunResult"/>s should be stored.
    /// </param>
    public DiskBasedResultStore(string storageRootPath)
    {
        storageRootPath = Path.GetFullPath(storageRootPath);
        _resultsRootPath = Path.Combine(storageRootPath, "results");
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ScenarioRunResult> ReadResultsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PathValidation.ValidatePathSegment(executionName, nameof(executionName));
        PathValidation.ValidatePathSegment(scenarioName, nameof(scenarioName));
        PathValidation.ValidatePathSegment(iterationName, nameof(iterationName));
        IEnumerable<FileInfo> resultFiles =
            EnumerateResultFiles(executionName, scenarioName, iterationName, cancellationToken);

        foreach (FileInfo resultFile in resultFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using FileStream stream = resultFile.OpenRead();

            ScenarioRunResult? result =
                await JsonSerializer.DeserializeAsync(
                    stream,
                    JsonUtilities.Default.ScenarioRunResultTypeInfo,
                    cancellationToken).ConfigureAwait(false);

            yield return result is null
                ? throw new JsonException(
                    string.Format(CultureInfo.CurrentCulture, DeserializationFailedMessage, resultFile.FullName))
                : result;
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteResultsAsync(
        IEnumerable<ScenarioRunResult> results,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(results);

        foreach (ScenarioRunResult result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PathValidation.ValidatePathSegment(result.ExecutionName, nameof(result.ExecutionName));
            PathValidation.ValidatePathSegment(result.ScenarioName, nameof(result.ScenarioName));
            PathValidation.ValidatePathSegment(result.IterationName, nameof(result.IterationName));

            var resultDir =
                new DirectoryInfo(PathValidation.EnsureWithinRoot(
                    _resultsRootPath,
                    Path.Combine(_resultsRootPath, result.ExecutionName, result.ScenarioName)));

            resultDir.Create();

            var resultFile = new FileInfo(Path.Combine(resultDir.FullName, $"{result.IterationName}.json"));

            using FileStream stream = resultFile.Create();

            await JsonSerializer.SerializeAsync(
                stream,
                result,
                JsonUtilities.Default.ScenarioRunResultTypeInfo,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DeleteResultsAsync(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken cancellationToken = default)
    {
        PathValidation.ValidatePathSegment(executionName, nameof(executionName));
        PathValidation.ValidatePathSegment(scenarioName, nameof(scenarioName));
        PathValidation.ValidatePathSegment(iterationName, nameof(iterationName));

        if (executionName is null && scenarioName is null && iterationName is null)
        {
            Directory.Delete(_resultsRootPath, recursive: true);
            _ = Directory.CreateDirectory(_resultsRootPath);
        }
        else if (executionName is not null && scenarioName is null && iterationName is null)
        {
            var executionDir = new DirectoryInfo(
                PathValidation.EnsureWithinRoot(
                    _resultsRootPath,
                    Path.Combine(_resultsRootPath, executionName)));

            if (executionDir.Exists)
            {
                executionDir.Delete(recursive: true);
            }
        }
        else if (executionName is not null && scenarioName is not null && iterationName is null)
        {
            var scenarioDir =
                new DirectoryInfo(
                    PathValidation.EnsureWithinRoot(
                        _resultsRootPath,
                        Path.Combine(_resultsRootPath, executionName, scenarioName)));

            if (scenarioDir.Exists)
            {
                scenarioDir.Delete(recursive: true);
            }
        }
        else if (executionName is not null && scenarioName is not null && iterationName is not null)
        {
            var resultFile =
                new FileInfo(
                    PathValidation.EnsureWithinRoot(
                        _resultsRootPath,
                        Path.Combine(_resultsRootPath, executionName, scenarioName, $"{iterationName}.json")));

            if (resultFile.Exists)
            {
                resultFile.Delete();
            }
        }
        else
        {
            IEnumerable<FileInfo> resultFiles =
                EnumerateResultFiles(executionName, scenarioName, iterationName, cancellationToken);

            foreach (FileInfo resultFile in resultFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DirectoryInfo scenarioDir = resultFile.Directory!;
                DirectoryInfo executionDir = scenarioDir.Parent!;

                resultFile.Delete();

                if (!scenarioDir.EnumerateFileSystemInfos().Any())
                {
                    scenarioDir.Delete(recursive: true);

                    if (!executionDir.EnumerateFileSystemInfos().Any())
                    {
                        executionDir.Delete(recursive: true);
                    }
                }
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GetLatestExecutionNamesAsync(
        int? count = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (count.HasValue && count <= 0)
        {
            yield break;
        }

        IEnumerable<DirectoryInfo> executionDirs = EnumerateExecutionDirs(cancellationToken: cancellationToken);

        if (count.HasValue)
        {
            executionDirs = executionDirs.Take(count.Value);
        }

        foreach (DirectoryInfo executionDir in executionDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return executionDir.Name;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GetScenarioNamesAsync(
        string executionName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PathValidation.ValidatePathSegment(executionName, nameof(executionName));

        IEnumerable<DirectoryInfo> executionDirs = EnumerateExecutionDirs(executionName, cancellationToken);

        IEnumerable<DirectoryInfo> scenarioDirs =
            EnumerateScenarioDirs(executionDirs, cancellationToken: cancellationToken);

        foreach (DirectoryInfo scenarioDir in scenarioDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return scenarioDir.Name;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GetIterationNamesAsync(
        string executionName,
        string scenarioName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PathValidation.ValidatePathSegment(executionName, nameof(executionName));
        PathValidation.ValidatePathSegment(scenarioName, nameof(scenarioName));

        IEnumerable<FileInfo> resultFiles =
            EnumerateResultFiles(executionName, scenarioName, cancellationToken: cancellationToken);

        foreach (FileInfo resultFile in resultFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return Path.GetFileNameWithoutExtension(resultFile.Name);
        }
    }

    private IEnumerable<DirectoryInfo> EnumerateExecutionDirs(
        string? executionName = null,
        CancellationToken cancellationToken = default)
    {
        var resultsDir = new DirectoryInfo(_resultsRootPath);
        if (!resultsDir.Exists)
        {
            yield break;
        }

        if (executionName is null)
        {
            IEnumerable<DirectoryInfo> executionDirs =
                resultsDir.EnumerateDirectories("*", InTopDirectoryOnly).OrderByDescending(d => d.CreationTimeUtc);

            foreach (DirectoryInfo executionDir in executionDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return executionDir;
            }
        }
        else
        {
            var executionDir = new DirectoryInfo(
                PathValidation.EnsureWithinRoot(
                    _resultsRootPath,
                    Path.Combine(_resultsRootPath, executionName)));
            if (executionDir.Exists)
            {
                yield return executionDir;
            }
        }
    }

#pragma warning disable SA1204 // Static elements should appear before instance elements.
    private static IEnumerable<DirectoryInfo> EnumerateScenarioDirs(
        IEnumerable<DirectoryInfo> executionDirs,
        string? scenarioName = null,
        CancellationToken cancellationToken = default)
#pragma warning restore SA1204
    {
        foreach (DirectoryInfo executionDir in executionDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (scenarioName is null)
            {
                IEnumerable<DirectoryInfo> scenarioDirs =
                    executionDir.EnumerateDirectories("*", InTopDirectoryOnly).OrderBy(d => d.Name);

                foreach (DirectoryInfo scenarioDir in scenarioDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return scenarioDir;
                }
            }
            else
            {
                var scenarioDir = new DirectoryInfo(Path.Combine(executionDir.FullName, scenarioName));
                if (scenarioDir.Exists)
                {
                    yield return scenarioDir;
                }
            }
        }
    }

#pragma warning disable SA1204 // Static elements should appear before instance elements.
    private static IEnumerable<FileInfo> EnumerateResultFiles(
        IEnumerable<DirectoryInfo> scenarioDirs,
        string? iterationName = null,
        CancellationToken cancellationToken = default)
#pragma warning restore SA1204
    {
        foreach (DirectoryInfo scenarioDir in scenarioDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (iterationName is null)
            {
                IEnumerable<FileInfo> resultFiles =
                    scenarioDir
                        .EnumerateFiles("*.json", InTopDirectoryOnly)
                        .OrderBy(f => Path.GetFileNameWithoutExtension(f.Name), IterationNameComparer.Default);

                foreach (FileInfo resultFile in resultFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return resultFile;
                }
            }
            else
            {
                var resultFile = new FileInfo(Path.Combine(scenarioDir.FullName, $"{iterationName}.json"));
                if (resultFile.Exists)
                {
                    yield return resultFile;
                }
            }
        }
    }

    private IEnumerable<FileInfo> EnumerateResultFiles(
        string? executionName = null,
        string? scenarioName = null,
        string? iterationName = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<DirectoryInfo> executionDirs = EnumerateExecutionDirs(executionName, cancellationToken);

        IEnumerable<DirectoryInfo> scenarioDirs =
            EnumerateScenarioDirs(executionDirs, scenarioName, cancellationToken);

        IEnumerable<FileInfo> resultFiles = EnumerateResultFiles(scenarioDirs, iterationName, cancellationToken);

        return resultFiles;
    }
}
