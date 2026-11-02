// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Reads Markdown content and converts it to an <see cref="IngestionDocument"/>.
/// </summary>
public sealed class MarkdownReader : IngestionDocumentReader
{
    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(FileInfo source, string identifier, string? mediaType = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

#if NET
        string fileContent = await File.ReadAllTextAsync(source.FullName, cancellationToken).ConfigureAwait(false);
#else
        using FileStream stream = new(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);
        string fileContent = await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
#endif
        return MarkdownParser.Parse(fileContent, identifier);
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

        string fileContent = await ReadToEndAsync(source, cancellationToken).ConfigureAwait(false);
        return MarkdownParser.Parse(fileContent, identifier);
    }

    private static async Task<string> ReadToEndAsync(Stream source, CancellationToken cancellationToken)
    {
        using StreamReader reader =
#if NET
            new(source, leaveOpen: true);
#else
            new(source, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
#endif

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
