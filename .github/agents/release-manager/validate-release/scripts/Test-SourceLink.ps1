#requires -Version 7.4
<#
.SYNOPSIS
Checks every lib DLL/TFM against original or msdl Portable PDBs.
.DESCRIPTION
Original mode checks symbols packages without contacting a server. Published mode uses
dotnet-symbol, then sourcelink test unless MetadataOnly is specified. Reports contain
package/TFM, artifact, DLL/PDB identities/hashes and tool output. Without ReportPath,
the complete JSON report is written to stdout. Exit 1 means a check
failed; exit 2 means the run could not complete. Metadata-only success is not URL validation.
.EXAMPLE
.\Test-SourceLink.ps1 -PackageDir C:\selected -Mode Original -SymbolsPackageDir C:\symbols -ReportPath C:\reports\original.json
.EXAMPLE
.\Test-SourceLink.ps1 -PackageDir C:\selected -ReportPath C:\reports\published.json
#>
[CmdletBinding()]
param(
    [string]$PackageDir = '.',
    [ValidateSet('Original', 'Published')][string]$Mode = 'Published',
    [string]$SymbolsPackageDir,
    [string]$SymbolServer = 'https://msdl.microsoft.com/download/symbols/',
    [switch]$MetadataOnly,
    [string]$ReportPath,
    [ValidateRange(0, [int]::MaxValue)][int]$Max = 0
)

$ErrorActionPreference = 'Stop'

if (-not ('ReleasePdbInspection' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

public sealed class ReleasePdbInspection
{
    public string Status { get; set; } = "metadata-valid";
    public string Detail { get; set; } = "";
    public string Guid { get; set; } = "";
    public string DllPdbGuid { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string[] SourceLink { get; set; } = Array.Empty<string>();
    public string[] ExternalDocuments { get; set; } = Array.Empty<string>();

    public static ReleasePdbInspection Read(string dll, string pdb)
    {
        var result = new ReleasePdbInspection();
        byte[] bytes = File.ReadAllBytes(pdb);
        result.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.Length < 4 || bytes[0] != 'B' || bytes[1] != 'S' || bytes[2] != 'J' || bytes[3] != 'B')
        {
            result.Status = "unsupported-pdb";
            result.Detail = "Not a Portable PDB; do not compare converted native PdbArtifacts.";
            return result;
        }
        using var peStream = File.OpenRead(dll);
        using var pe = new PEReader(peStream);
        using var pdbStream = File.OpenRead(pdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        var reader = provider.GetMetadataReader();
        var id = new BlobContentId(reader.DebugMetadataHeader.Id);
        result.Guid = id.Guid.ToString();
        var entries = pe.ReadDebugDirectory();
        var codeViews = entries.Where(e => e.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (codeViews.Length == 1)
            result.DllPdbGuid = pe.ReadCodeViewDebugDirectoryData(codeViews[0]).Guid.ToString();
        if (codeViews.Length != 1 ||
            pe.ReadCodeViewDebugDirectoryData(codeViews[0]).Guid != id.Guid ||
            pe.ReadCodeViewDebugDirectoryData(codeViews[0]).Age != 1 ||
            codeViews[0].Stamp != id.Stamp)
        {
            result.Status = "identity-mismatch";
            result.Detail = "DLL CodeView GUID/stamp/age does not match Portable PDB content ID.";
            return result;
        }
        var checksums = entries.Where(e => e.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
        if (checksums.Length != 1)
        {
            result.Status = "identity-mismatch";
            result.Detail = "Expected one DLL PDB checksum entry.";
            return result;
        }
        var checksum = pe.ReadPdbChecksumDebugDirectoryData(checksums[0]);
        using var hash = IncrementalHash.CreateHash(new HashAlgorithmName(checksum.AlgorithmName));
        // Portable PDB checksums are calculated with the 20-byte content ID zeroed.
        int idOffset = reader.DebugMetadataHeader.IdStartOffset;
        var hashBytes = (byte[])bytes.Clone();
        Array.Clear(hashBytes, idOffset, 20);
        hash.AppendData(hashBytes);
        if (!hash.GetHashAndReset().SequenceEqual(checksum.Checksum))
        {
            result.Status = "identity-mismatch";
            result.Detail = "Portable PDB checksum does not match the DLL debug entry.";
            return result;
        }
        var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
        var embeddedKind = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
        result.SourceLink = reader.CustomDebugInformation
            .Select(h => reader.GetCustomDebugInformation(h))
            .Where(c => reader.GetGuid(c.Kind) == sourceLinkKind)
            .Select(c => System.Text.Encoding.UTF8.GetString(reader.GetBlobBytes(c.Value))).ToArray();
        result.ExternalDocuments = reader.Documents.Where(h =>
            !reader.GetCustomDebugInformation(h).Any(c =>
                reader.GetGuid(reader.GetCustomDebugInformation(c).Kind) == embeddedKind))
            .Select(h => reader.GetString(reader.GetDocument(h).Name)).ToArray();
        return result;
    }
}
'@
}

function Resolve-ReleaseTool {
    param([string]$Tool)
    $command = Get-Command -Name $Tool -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        $toolPath = $command.Source
    }
    else {
        $toolName = if ($IsWindows) { "$Tool.exe" } else { $Tool }
        $toolPath = Join-Path (Join-Path $HOME '.dotnet') (Join-Path 'tools' $toolName)
        if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
            throw "Required tool '$Tool' was not found on PATH or at '$toolPath'. Install it with dotnet tool install -g $Tool."
        }
    }
    return $toolPath
}

function Invoke-ReleaseTool {
    param([string]$Tool, [string[]]$Arguments)
    $toolPath = Resolve-ReleaseTool -Tool $Tool
    $output = & $toolPath @Arguments 2>&1 | Out-String
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Test-ReleasePdb {
    param([string]$Dll, [string]$Pdb, [switch]$CheckMetadataOnly)

    try {
        $inspection = [ReleasePdbInspection]::Read($Dll, $Pdb)
    }
    catch [BadImageFormatException] {
        return [pscustomobject]@{
            Result = 'metadata-invalid'; Detail = "Unreadable DLL/Portable PDB: $($_.Exception.Message)"
            PdbSha256 = (Get-FileHash -LiteralPath $Pdb -Algorithm SHA256).Hash
        }
    }
    $result = [ordered]@{
        Result = $inspection.Status
        Detail = $inspection.Detail
        PdbGuid = $inspection.Guid
        DllPdbGuid = $inspection.DllPdbGuid
        PdbSha256 = $inspection.Sha256
        SourceLinkBytes = 0
        ExternalDocuments = $inspection.ExternalDocuments.Count
        ToolOutput = ''
    }
    if ($inspection.Status -ne 'metadata-valid') { return [pscustomobject]$result }
    if ($inspection.SourceLink.Count -gt 1) {
        $result.Result = 'metadata-invalid'
        $result.Detail = 'Multiple Source Link records are ambiguous.'
        return [pscustomobject]$result
    }
    if ($inspection.SourceLink.Count -eq 0 -or [string]::IsNullOrWhiteSpace($inspection.SourceLink[0])) {
        $result.Result = 'metadata-empty'
        $result.Detail = 'Expected exactly one nonempty Source Link payload.'
        return [pscustomobject]$result
    }
    $result.SourceLinkBytes = [Text.Encoding]::UTF8.GetByteCount($inspection.SourceLink[0])
    try {
        $mapping = ConvertFrom-Json -InputObject $inspection.SourceLink[0] -AsHashtable
    }
    catch [ArgumentException] {
        $result.Result = 'metadata-invalid'
        $result.Detail = "Invalid Source Link JSON: $($_.Exception.Message)"
        return [pscustomobject]$result
    }
    if ($mapping -isnot [System.Collections.IDictionary] -or
        $mapping.documents -isnot [System.Collections.IDictionary] -or $mapping.documents.Count -eq 0) {
        $result.Result = 'metadata-invalid'
        $result.Detail = 'Source Link documents mapping is missing or empty.'
        return [pscustomobject]$result
    }
    foreach ($entry in $mapping.documents.GetEnumerator()) {
        $uri = $null
        if ($entry.Value -isnot [string] -or
            -not [Uri]::TryCreate($entry.Value, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -ne 'https' -or
            ([regex]::Matches($entry.Key, '\*').Count -gt 1) -or
            ($entry.Key.Contains('*') -and -not $entry.Key.EndsWith('*')) -or
            ([regex]::Matches($entry.Key, '\*').Count -ne [regex]::Matches($entry.Value, '\*').Count)) {
            $result.Result = 'metadata-invalid'
            $result.Detail = "Unusable Source Link mapping: $($entry.Key)"
            return [pscustomobject]$result
        }
    }
    foreach ($document in $inspection.ExternalDocuments) {
        $covered = @($mapping.documents.Keys | Where-Object {
            $document -match ('^' + [regex]::Escape($_).Replace('\*', '.*') + '$')
        })
        if ($covered.Count -eq 0) {
            $result.Result = 'metadata-invalid'
            $result.Detail = "Nonembedded document is unmapped: $document"
            return [pscustomobject]$result
        }
    }
    if ($CheckMetadataOnly) { return [pscustomobject]$result }
    $tool = Invoke-ReleaseTool -Tool sourcelink -Arguments @('test', $Pdb)
    $result.ToolOutput = $tool.Output
    if ($tool.ExitCode -eq 0) {
        $result.Result = 'valid'
    }
    elseif ($tool.Output -match '(?i)(checksum|hash).*(mismatch|does not match|did not match)|does not match.*(checksum|hash)') {
        $result.Result = 'checksum-mismatch'
    }
    elseif ($tool.Output -match '(?i)\b(404|403|401)\b|name or service not known|no such host|unable to.*(download|resolve)|failed to download') {
        $result.Result = 'source-url-unreachable'
    }
    else {
        $result.Result = 'sourcelink-failed'
    }
    $result.Detail = "sourcelink test exit $($tool.ExitCode); see ToolOutput."
    return [pscustomobject]$result
}

function Invoke-SourceLinkSweep {
    param(
        [string]$Packages, [string]$CheckMode, [string]$Symbols,
        [string]$Server, [switch]$CheckMetadataOnly, [int]$Limit
    )

    $pkgs = @(Get-ChildItem -LiteralPath $Packages -Filter '*.nupkg' -File |
        Where-Object { $_.Name -notlike '*.symbols.nupkg' } | Sort-Object Name)
    if ($pkgs.Count -eq 0) { throw "No .nupkg files in '$Packages'." }
    if ($CheckMode -eq 'Original' -and -not (Test-Path -LiteralPath $Symbols -PathType Container)) {
        throw 'Original mode requires an existing SymbolsPackageDir.'
    }
    if ($Limit -gt 0) { $pkgs = @($pkgs | Select-Object -First $Limit) }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('srclink-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        foreach ($pkg in $pkgs) {
            $ed = Join-Path $tmp $pkg.BaseName
            Expand-Archive -LiteralPath $pkg.FullName -DestinationPath $ed
            $nuspecs = @(Get-ChildItem -LiteralPath $ed -Filter '*.nuspec' -File)
            if ($nuspecs.Count -ne 1) { throw "Expected one nuspec in '$($pkg.Name)'." }
            [xml]$nuspec = Get-Content -LiteralPath $nuspecs[0].FullName -Raw
            $id = [string]$nuspec.package.metadata.id
            $version = [string]$nuspec.package.metadata.version
            if (-not $id -or -not $version) { throw "Missing package ID/version in '$($pkg.Name)'." }
            $base = [ordered]@{
                Package = $id; Version = $version; PackageArtifact = $pkg.FullName
                PackageSha256 = (Get-FileHash -LiteralPath $pkg.FullName -Algorithm SHA256).Hash
                SourceCommit = [string]$nuspec.package.metadata.repository.commit
                Mode = $CheckMode; MetadataOnly = ($CheckMode -eq 'Original' -or $CheckMetadataOnly.IsPresent)
                Tfm = ''; Dll = ''; DllSha256 = ''; PdbArtifact = ''; PdbEntry = ''; PdbLength = 0
                SymbolsPackageSha256 = ''
                DownloadOutput = ''; Result = ''; Detail = ''
            }
            $lib = Join-Path $ed 'lib'
            $dlls = if (Test-Path -LiteralPath $lib) {
                @(Get-ChildItem -LiteralPath $lib -Filter '*.dll' -Recurse -File)
            } else { @() }
            if ($dlls.Count -eq 0) {
                $base.Result = 'no-lib-dll'
                [pscustomobject]$base
                continue
            }
            $symbolsRoot = Join-Path $ed 'original-symbols'
            $symbolsArchive = $null
            $symbolsError = ''
            if ($CheckMode -eq 'Original') {
                $symbolsArchive = Join-Path $Symbols "$id.$version.symbols.nupkg"
                if (Test-Path -LiteralPath $symbolsArchive) {
                    $base.SymbolsPackageSha256 = (Get-FileHash -LiteralPath $symbolsArchive -Algorithm SHA256).Hash
                    Expand-Archive -LiteralPath $symbolsArchive -DestinationPath $symbolsRoot
                    $symbolNuspecs = @(Get-ChildItem -LiteralPath $symbolsRoot -Filter '*.nuspec' -File)
                    if ($symbolNuspecs.Count -ne 1) {
                        $symbolsError = 'Expected one original symbols-package nuspec.'
                    }
                    else {
                        [xml]$symbolNuspec = Get-Content -LiteralPath $symbolNuspecs[0].FullName -Raw
                        if ($symbolNuspec.package.metadata.id -ne $id -or
                            $symbolNuspec.package.metadata.version -ne $version -or
                            [string]$symbolNuspec.package.metadata.repository.commit -ne $base.SourceCommit) {
                            $symbolsError = 'Original symbols package ID/version/source commit differs from selected package.'
                        }
                    }
                }
            }
            foreach ($dll in $dlls) {
                $row = [ordered]@{}
                foreach ($key in $base.Keys) { $row[$key] = $base[$key] }
                $relative = [IO.Path]::GetRelativePath($ed, $dll.FullName)
                $row.Tfm = [IO.Path]::GetRelativePath($lib, $dll.DirectoryName)
                $row.Dll = $relative
                $row.DllSha256 = (Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256).Hash
                if ($CheckMode -eq 'Original') {
                    $row.PdbArtifact = $symbolsArchive
                    $row.PdbEntry = [IO.Path]::ChangeExtension($relative, '.pdb')
                    $pdb = Join-Path $symbolsRoot $row.PdbEntry
                    if ($symbolsError) {
                        $row.Result = 'artifact-mismatch'
                        $row.Detail = $symbolsError
                        [pscustomobject]$row
                        continue
                    }
                }
                else {
                    $sd = Join-Path $ed ('download-' + [guid]::NewGuid().ToString('N'))
                    New-Item -ItemType Directory -Path $sd | Out-Null
                    $download = Invoke-ReleaseTool -Tool dotnet-symbol -Arguments @(
                        '--symbols', '--server-path', $Server, '--output', $sd, $dll.FullName)
                    $row.DownloadOutput = $download.Output
                    $row.PdbArtifact = $Server
                    $row.PdbEntry = [IO.Path]::ChangeExtension($dll.Name, '.pdb')
                    $pdb = Join-Path $sd $row.PdbEntry
                    if ($download.ExitCode -ne 0) {
                        $row.Result = 'symbol-download-failed'
                        $row.Detail = "dotnet-symbol exit $($download.ExitCode); see DownloadOutput."
                        [pscustomobject]$row
                        continue
                    }
                }
                if (-not (Test-Path -LiteralPath $pdb -PathType Leaf)) {
                    $row.Result = 'symbols-not-found'
                    $row.Detail = 'No matching PDB at the expected artifact entry; inspect transport/artifact evidence.'
                    [pscustomobject]$row
                    continue
                }
                $row.PdbLength = (Get-Item -LiteralPath $pdb).Length
                $check = Test-ReleasePdb -Dll $dll.FullName -Pdb $pdb -CheckMetadataOnly:$row.MetadataOnly
                foreach ($property in $check.PSObject.Properties) { $row[$property.Name] = $property.Value }
                [pscustomobject]$row
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $report = [ordered]@{
        Mode = $Mode; MetadataOnly = ($Mode -eq 'Original' -or $MetadataOnly.IsPresent)
        Partial = ($Max -gt 0); Results = @(); Errors = @(); Succeeded = $false
    }
    $exitCode = 2
    $rows = [Collections.Generic.List[object]]::new()
    try {
        Invoke-SourceLinkSweep -Packages $PackageDir -CheckMode $Mode -Symbols $SymbolsPackageDir `
            -Server $SymbolServer -CheckMetadataOnly:$MetadataOnly -Limit $Max |
            ForEach-Object { $rows.Add($_) }
        $failures = @($rows | Where-Object { $_.Result -notin @('valid', 'metadata-valid', 'no-lib-dll') })
        $report.Succeeded = ($failures.Count -eq 0 -and -not $report.Partial)
        $exitCode = if ($report.Succeeded) { 0 } else { 1 }
    }
    catch {
        $report.Errors = @($_.Exception.Message)
        [Console]::Error.WriteLine("Verification could not complete: $($_.Exception.Message)")
    }
    finally {
        $report.Results = $rows.ToArray()
        if ($ReportPath) {
            $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding utf8
            $report.Results | Select-Object Package, Version, Tfm, Result, Detail | Format-Table -AutoSize | Out-Host
        }
        else {
            $report | ConvertTo-Json -Depth 12 | Write-Output
        }
        if ($report.Partial) { [Console]::Error.WriteLine('Partial sweep (-Max): not release sign-off evidence.') }
    }
    exit $exitCode
}
