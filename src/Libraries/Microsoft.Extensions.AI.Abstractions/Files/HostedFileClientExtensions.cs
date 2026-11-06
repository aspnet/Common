// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Extension methods for <see cref="IHostedFileClient"/>.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AIFiles, UrlFormat = DiagnosticIds.UrlFormat)]
public static class HostedFileClientExtensions
{
    /// <summary>
    /// Uploads content from a <see cref="DataContent"/>.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <param name="content">The content to upload.</param>
    /// <param name="options">Options to configure the upload.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>Information about the uploaded file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public static Task<HostedFileContent> UploadAsync(
        this IHostedFileClient client,
        DataContent content,
        HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(client);
        _ = Throw.IfNull(content);

        MemoryStream stream = MemoryMarshal.TryGetArray(content.Data, out ArraySegment<byte> arraySegment) ?
            new(arraySegment.Array!, arraySegment.Offset, arraySegment.Count) :
            new(content.Data.ToArray());

        return client.UploadAsync(stream, content.MediaType, content.Name, options, cancellationToken);
    }

    /// <summary>
    /// Uploads a file from a local file path.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <param name="filePath">The path to the file to upload.</param>
    /// <param name="options">Options to configure the upload.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>Information about the uploaded file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
    public static async Task<HostedFileContent> UploadAsync(
        this IHostedFileClient client,
        string filePath,
        HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(client);
        _ = Throw.IfNullOrEmpty(filePath);

        string? mediaType = MediaTypeMap.GetMediaType(filePath);
        string fileName = Path.GetFileName(filePath);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 4096, useAsync: true);

        return await client.UploadAsync(stream, mediaType, fileName, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a file and saves it to a local path.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <param name="fileId">The ID of the file to download.</param>
    /// <param name="destinationPath">
    /// The path to save the file to. If the path is a directory or empty, the file name will be inferred.
    /// An empty path is treated as the current directory.
    /// </param>
    /// <param name="options">Options to configure the download.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The actual path where the file was saved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fileId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="destinationPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">A file at the destination path already exists.</exception>
    public static async Task<string> DownloadToAsync(
        this IHostedFileClient client,
        string fileId,
        string destinationPath,
        HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(client);
        _ = Throw.IfNullOrWhitespace(fileId);
        _ = Throw.IfNull(destinationPath);

        using HostedFileDownloadStream downloadStream = await client.DownloadAsync(fileId, options, cancellationToken).ConfigureAwait(false);

        // Determine the final path. If the path is empty, treat it as the current directory.
        string finalPath = destinationPath;
        if (destinationPath.Length == 0 || Directory.Exists(destinationPath))
        {
            string? fileName = null;

            if (downloadStream.FileName is not null)
            {
                fileName = Path.GetFileName(downloadStream.FileName);
            }

            if (string.IsNullOrEmpty(fileName))
            {
                string? extension = downloadStream.MediaType is string mediaType ? MediaTypeMap.GetExtension(mediaType) : null;
                fileName = $"{fileId}{extension}";
            }

            finalPath = destinationPath.Length == 0 ? fileName! : Path.Combine(destinationPath, fileName);
        }

        using FileStream fileStream = new(
            finalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 1, // buffering in FileStream is a nop as CopyToAsync will use its own, larger buffer
            useAsync: true);

        await downloadStream.CopyToAsync(fileStream,
#if !NET
            81920,
#endif
            cancellationToken).ConfigureAwait(false);

        return finalPath;
    }

    /// <summary>
    /// Downloads a file referenced by a <see cref="HostedFileContent"/>.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <param name="hostedFile">The hosted file reference.</param>
    /// <param name="options">Options to configure the download.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="HostedFileDownloadStream"/> containing the file content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="hostedFile"/> is <see langword="null"/>.</exception>
    public static Task<HostedFileDownloadStream> DownloadAsync(
        this IHostedFileClient client,
        HostedFileContent hostedFile,
        HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(client);
        _ = Throw.IfNull(hostedFile);

        if (hostedFile.Scope is string scope && options?.Scope is null)
        {
            options = options?.Clone() ?? new();
            options.Scope = scope;
        }

        return client.DownloadAsync(hostedFile.FileId, options, cancellationToken);
    }

    /// <summary>
    /// Downloads a file and returns its content as a buffered <see cref="DataContent"/>.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <param name="fileId">The ID of the file to download.</param>
    /// <param name="options">Options to configure the download.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The file content as a <see cref="DataContent"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fileId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is empty or whitespace.</exception>
    /// <remarks>
    /// This method buffers the entire file content into memory. For large files,
    /// consider using <see cref="IHostedFileClient.DownloadAsync"/>
    /// and streaming directly to the destination.
    /// </remarks>
    public static async Task<DataContent> DownloadAsDataContentAsync(
        this IHostedFileClient client,
        string fileId,
        HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(client);
        _ = Throw.IfNullOrWhitespace(fileId);

        using HostedFileDownloadStream downloadStream = await client.DownloadAsync(fileId, options, cancellationToken).ConfigureAwait(false);

        return await downloadStream.ToDataContentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the <see cref="HostedFileClientMetadata"/> for this client.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <returns>The metadata for this client, or <see langword="null"/> if not available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public static HostedFileClientMetadata? GetMetadata(this IHostedFileClient client)
    {
        _ = Throw.IfNull(client);

        return client.GetService(typeof(HostedFileClientMetadata)) as HostedFileClientMetadata;
    }

    /// <summary>
    /// Gets a service of the specified type from the file client.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve.</typeparam>
    /// <param name="client">The file client.</param>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found service, or <see langword="null"/> if not available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public static TService? GetService<TService>(this IHostedFileClient client, object? serviceKey = null)
    {
        _ = Throw.IfNull(client);

        return client.GetService(typeof(TService), serviceKey) is TService service ? service : default;
    }

    /// <summary>
    /// Asks the <see cref="IHostedFileClient"/> for an object of the specified type <paramref name="serviceType"/>
    /// and throws an exception if one isn't available.
    /// </summary>
    /// <param name="client">The file client.</param>
    /// <param name="serviceType">The type of object being requested.</param>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No service of the requested type for the specified key is available.</exception>
    /// <remarks>
    /// The purpose of this method is to allow for the retrieval of services that are required to be provided by the <see cref="IHostedFileClient"/>,
    /// including itself or any services it might be wrapping.
    /// </remarks>
    public static object GetRequiredService(this IHostedFileClient client, Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(client);
        _ = Throw.IfNull(serviceType);

        return
            client.GetService(serviceType, serviceKey) ??
            throw Throw.CreateMissingServiceException(serviceType, serviceKey);
    }

    /// <summary>
    /// Asks the <see cref="IHostedFileClient"/> for an object of type <typeparamref name="TService"/>
    /// and throws an exception if one isn't available.
    /// </summary>
    /// <typeparam name="TService">The type of the object to be retrieved.</typeparam>
    /// <param name="client">The file client.</param>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No service of the requested type for the specified key is available.</exception>
    /// <remarks>
    /// The purpose of this method is to allow for the retrieval of strongly typed services that are required to be provided by the <see cref="IHostedFileClient"/>,
    /// including itself or any services it might be wrapping.
    /// </remarks>
    public static TService GetRequiredService<TService>(this IHostedFileClient client, object? serviceKey = null)
    {
        _ = Throw.IfNull(client);

        if (client.GetService(typeof(TService), serviceKey) is not TService service)
        {
            throw Throw.CreateMissingServiceException(typeof(TService), serviceKey);
        }

        return service;
    }
}
