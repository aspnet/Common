// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Files.DataLake;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit;

namespace Microsoft.Extensions.AI.Evaluation.Reporting.Tests;

public class AzureResultStoreTests : ResultStoreTester, IAsyncLifetime
{
    private static readonly DataLakeFileSystemClient? _fsClient;

    static AzureResultStoreTests()
    {
        if (Settings.Current.Configured)
        {
            var credential = new ChainedTokenCredential(new AzureCliCredential(), new DefaultAzureCredential());
            _fsClient = new(
                new Uri(
                    baseUri: new Uri(Settings.Current.StorageAccountEndpoint),
                    relativeUri: Settings.Current.StorageContainerName),
                credential);
        }
    }

    private readonly DataLakeDirectoryClient? _dirClient;

    public AzureResultStoreTests()
    {
        _dirClient = _fsClient?.GetDirectoryClient(Path.GetRandomFileName());
    }

    public ValueTask InitializeAsync()
    {
#if NET
        return ValueTask.CompletedTask;
#else
        return new ValueTask(Task.CompletedTask);
#endif
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_dirClient is not null)
        {
            await _dirClient.DeleteAsync();
        }
    }

    public override bool IsConfigured => Settings.Current.Configured;

    public override IEvaluationResultStore CreateResultStore()
        => new AzureStorageResultStore(_dirClient!);

}
