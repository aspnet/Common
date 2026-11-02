// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Reads documents by converting them to Markdown using the <see href="https://github.com/microsoft/markitdown">MarkItDown</see> tool.
/// </summary>
public class MarkItDownReader : IngestionDocumentReader
{
    private readonly FileInfo? _exePath;
    private readonly bool _extractImages;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkItDownReader"/> class.
    /// </summary>
    /// <param name="exePath">The path to the MarkItDown executable. When not provided, "markitdown" needs to be added to PATH.</param>
    /// <param name="extractImages">A value indicating whether to extract images.</param>
    public MarkItDownReader(FileInfo? exePath = null, bool extractImages = false)
    {
        _exePath = exePath;
        _extractImages = extractImages;
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(FileInfo source, string identifier, string? mediaType = null, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

        if (!source.Exists)
        {
            throw new FileNotFoundException("The specified file does not exist.", source.FullName);
        }

        // Manually set ProcessStartInfo.WorkingDirectory to a "safe location":
        // - If exePath is provided, use its directory.
        // - Otherwise, use AppContext.BaseDirectory (the directory of the running application).
        string workingDirectory = _exePath?.Directory?.FullName ?? AppContext.BaseDirectory;

        ProcessStartInfo startInfo = new()
        {
            FileName = _exePath?.FullName ?? "markitdown",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        // Force UTF-8 encoding in the environment (will produce garbage otherwise).
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        startInfo.Environment["LANG"] = "C.UTF-8";

#if NET
        startInfo.ArgumentList.Add(source.FullName);
        if (_extractImages)
        {
            startInfo.ArgumentList.Add("--keep-data-uris");
        }
#else
        startInfo.Arguments = $"\"{source.FullName}\"" + (_extractImages ? " --keep-data-uris" : string.Empty);
#endif

        string outputContent = string.Empty;
        using (Process process = new() { StartInfo = startInfo })
        {
            process.Start();

            outputContent = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#if NET
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else
            process.WaitForExit();
#endif

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"MarkItDown process failed with exit code {process.ExitCode}.");
            }
        }

        return MarkdownParser.Parse(outputContent, identifier);
    }

    /// <inheritdoc/>
    /// <remarks>The contents of <paramref name="source"/> are copied to a temporary file.</remarks>
    public override async Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);

        // Instead of creating a temporary file, we could write to the StandardInput of the process.
        // MarkItDown says it supports reading from stdin, but it does not work as expected.
        // Even the sample command line does not work with stdin: "cat example.pdf | markitdown"
        // I can be doing something wrong, but for now, let's write to a temporary file.
        string inputFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        FileStream inputFile = new(inputFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);

        try
        {
            await source
#if NET
                .CopyToAsync(inputFile, cancellationToken)
#else
                .CopyToAsync(inputFile)
#endif
                .ConfigureAwait(false);

            inputFile.Close();

            return await ReadAsync(new FileInfo(inputFilePath), identifier, mediaType, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
#if NET
            await inputFile.DisposeAsync().ConfigureAwait(false);
#else
            inputFile.Dispose();
#endif
            File.Delete(inputFilePath);
        }
    }
}
