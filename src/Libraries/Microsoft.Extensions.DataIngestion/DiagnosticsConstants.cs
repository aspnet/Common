// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.DataIngestion;

internal static class DiagnosticsConstants
{
    internal const string ActivitySourceName = "Experimental.Microsoft.Extensions.DataIngestion";
    internal const string ErrorTypeTagName = "error.type";

    internal static class ProcessDirectory
    {
        internal const string ActivityName = "ProcessDirectory";
        internal const string DirectoryPathTagName = "rag.directory.path";
        internal const string SearchPatternTagName = "rag.directory.search.pattern";
        internal const string SearchOptionTagName = "rag.directory.search.option";
    }

    internal static class ProcessFiles
    {
        internal const string ActivityName = "ProcessFiles";
        internal const string FileCountTagName = "rag.file.count";
    }

    internal static class ProcessSource
    {
        internal const string ActivityName = "ProcessDocument";
        internal const string DocumentIdTagName = "rag.document.id";
    }

    internal static class ProcessFile
    {
        internal const string ActivityName = "ProcessFile";
        internal const string FilePathTagName = "rag.file.path";
    }
}
