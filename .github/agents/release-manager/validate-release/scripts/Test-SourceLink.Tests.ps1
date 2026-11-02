#requires -Version 7.4
#requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# Run with Pester 5: Invoke-Pester .\.github\agents\release-manager\validate-release\scripts\Test-SourceLink.Tests.ps1
# Fixtures are compiled locally; symbol downloads and source checks are mocked.

Describe 'Release Source Link verification' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'Test-SourceLink.ps1')

        $pwsh = (Get-Process -Id $PID).Path
        $sdk = @(& dotnet --list-sdks | ForEach-Object {
            if ($_ -match '^(\S+) \[(.+)\]$') {
                Join-Path $Matches[2] "$($Matches[1])\Roslyn\bincore\csc.dll"
            }
        } | Where-Object { Test-Path -LiteralPath $_ })[-1]
        if (-not $sdk) { throw 'A local .NET SDK C# compiler is required for offline fixtures.' }

        function New-PdbFixture {
            param([string]$Name, [string]$Json, [string]$Source, [switch]$NoSourceLink, [switch]$Embed)
            $dir = Join-Path $TestDrive $Name
            New-Item -ItemType Directory -Path $dir | Out-Null
            $cs = Join-Path $dir 'Fixture.cs'
            $dll = Join-Path $dir 'Fixture.dll'
            $jsonPath = Join-Path $dir 'sourcelink.json'
            Set-Content -LiteralPath $cs -Value $Source
            Set-Content -LiteralPath $jsonPath -Value $Json -NoNewline
            [string[]]$sourceLinkArg = if ($NoSourceLink) { @() } else { @("/sourcelink:$jsonPath") }
            if ($Embed) { $sourceLinkArg += "/embed:$cs" }
            $compilerOutput = & dotnet $sdk /nologo /target:library /debug:portable /deterministic+ `
                "/reference:$([object].Assembly.Location)" "/out:$dll" @sourceLinkArg `
                "/pathmap:$dir=/_/" $cs 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw "Fixture compilation failed: $compilerOutput" }
            [pscustomobject]@{ Dll = $dll; Pdb = [IO.Path]::ChangeExtension($dll, '.pdb') }
        }

        function New-TestPackage {
            param([string]$Root, [string]$Id, [string[]]$Tfms, $Fixture, [switch]$Symbols,
                [string]$SourceCommit = 'fixture-sha')
            $content = Join-Path $Root ([guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $content -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $content "$Id.nuspec") -Value @"
<package><metadata><id>$Id</id><version>1.0.0</version><repository commit="$SourceCommit" /></metadata></package>
"@
            foreach ($tfm in $Tfms) {
                $lib = Join-Path $content "lib\$tfm"
                New-Item -ItemType Directory -Path $lib -Force | Out-Null
                Copy-Item -LiteralPath $Fixture.Dll -Destination $lib
                if ($Symbols) { Copy-Item -LiteralPath $Fixture.Pdb -Destination $lib }
            }
            $suffix = if ($Symbols) { '.symbols' } else { '' }
            $archive = Join-Path $Root "$Id.1.0.0$suffix.nupkg"
            [IO.Compression.ZipFile]::CreateFromDirectory($content, $archive)
            Remove-Item -LiteralPath $content -Recurse -Force
            $archive
        }

        $validJson = '{"documents":{"/_/*":"https://example.invalid/source/*"}}'
        $valid = New-PdbFixture valid $validJson 'public class Fixture { public int Value => 42; }'
        $other = New-PdbFixture other $validJson 'public class Fixture { public int Value => 43; }'
        $empty = New-PdbFixture empty '' 'public class Fixture { public int Value => 42; }'
        $absent = New-PdbFixture absent '' 'public class Fixture { }' -NoSourceLink
        $malformed = New-PdbFixture malformed 'not JSON' 'public class Fixture { }'
        $badUri = New-PdbFixture badUri '{"documents":{"/_/*":"not-a-url/*"}}' 'public class Fixture { }'
        $unmapped = New-PdbFixture unmapped '{"documents":{"/missing/*":"https://example.invalid/*"}}' 'public class Fixture { }'
        $embedded = New-PdbFixture embedded '{"documents":{"/missing/*":"https://example.invalid/*"}}' 'public class Fixture { }' -Embed
        $nonfinalWildcard = New-PdbFixture nonfinalWildcard '{"documents":{"/_/*Fixture.cs":"https://example.invalid/*"}}' 'public class Fixture { }'
        $packages = Join-Path $TestDrive 'packages'
        $symbols = Join-Path $TestDrive 'symbols'
        New-Item -ItemType Directory -Path $packages, $symbols | Out-Null
        $null = New-TestPackage $packages Library @('net8.0', 'net10.0') $valid
        $null = New-TestPackage $symbols Library @('net8.0', 'net10.0') $valid -Symbols
        $null = New-TestPackage $packages Template @() $valid
        $helper = Join-Path $PSScriptRoot 'Test-SourceLink.ps1'
    }

    BeforeEach {
        Mock Invoke-ReleaseTool { throw 'Unexpected external operation in offline test.' }
    }

    It 'validates original Portable PDB GUID, stamp, checksum and mapping without network calls' {
        $result = Test-ReleasePdb $valid.Dll $valid.Pdb -CheckMetadataOnly
        $result.Result | Should -Be 'metadata-valid'
        $result.SourceLinkBytes | Should -BeGreaterThan 0
        $result.PdbGuid | Should -Not -BeNullOrEmpty
        Should -Invoke Invoke-ReleaseTool -Times 0
    }

    It 'rejects a different Portable PDB even with the same filename' {
        (Test-ReleasePdb $valid.Dll $other.Pdb -CheckMetadataOnly).Result | Should -Be 'identity-mismatch'
    }

    It 'checks the PDB checksum even if the content ID was not changed' {
        $tampered = Join-Path $TestDrive 'tampered.pdb'
        $bytes = [IO.File]::ReadAllBytes($valid.Pdb)
        $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
        [IO.File]::WriteAllBytes($tampered, $bytes)
        $result = Test-ReleasePdb $valid.Dll $tampered -CheckMetadataOnly
        $result.Result | Should -Be 'identity-mismatch'
        $result.Detail | Should -Match 'checksum'
    }

    It 'detects a zero-byte Source Link record instead of treating presence as usable' {
        $inspection = [ReleasePdbInspection]::Read($empty.Dll, $empty.Pdb)
        $inspection.SourceLink.Count | Should -Be 1
        $inspection.SourceLink[0].Length | Should -Be 0
        (Test-ReleasePdb $empty.Dll $empty.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-empty'
    }

    It 'rejects mappings that leave nonembedded source documents uncovered' {
        (Test-ReleasePdb $unmapped.Dll $unmapped.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-invalid'
    }

    It 'does not require an external URL mapping for embedded documents' {
        $result = Test-ReleasePdb $embedded.Dll $embedded.Pdb -CheckMetadataOnly
        $result.Result | Should -Be 'metadata-valid'
        $result.ExternalDocuments | Should -Be 0
    }

    It 'rejects missing Source Link metadata' {
        (Test-ReleasePdb $absent.Dll $absent.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-empty'
    }

    It 'reports malformed JSON as a per-asset metadata failure' {
        (Test-ReleasePdb $malformed.Dll $malformed.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-invalid'
    }

    It 'rejects unusable Source Link URL mappings' {
        (Test-ReleasePdb $badUri.Dll $badUri.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-invalid'
    }

    It 'rejects a nonfinal wildcard even when it matches a source document' {
        (Test-ReleasePdb $nonfinalWildcard.Dll $nonfinalWildcard.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-invalid'
    }

    It 'matches document paths case-insensitively' {
        $caseOnly = New-PdbFixture caseOnly '{"documents":{"/_/fixture.CS":"https://example.invalid/Fixture.cs"}}' 'public class Fixture { }'
        (Test-ReleasePdb $caseOnly.Dll $caseOnly.Pdb -CheckMetadataOnly).Result | Should -Be 'metadata-valid'
    }

    It 'prefers an executable already on PATH' {
        Mock Get-Command { [pscustomobject]@{ Source = $pwsh } } -ParameterFilter { $Name -eq 'sourcelink' }
        (Resolve-ReleaseTool sourcelink) | Should -Be $pwsh
    }

    It 'finds installed global tools when they are not on PATH' {
        Mock Get-Command { $null } -ParameterFilter { $Name -in @('sourcelink', 'dotnet-symbol') }
        Mock Test-Path { $true }
        foreach ($tool in @('sourcelink', 'dotnet-symbol')) {
            $name = if ($IsWindows) { "$tool.exe" } else { $tool }
            $expected = Join-Path (Join-Path $HOME '.dotnet') (Join-Path 'tools' $name)
            (Resolve-ReleaseTool $tool) | Should -Be $expected
        }
    }

    It 'reports absent global tools explicitly without attempting a download' {
        Mock Get-Command { $null } -ParameterFilter { $Name -eq 'sourcelink' }
        Mock Test-Path { $false }
        { Resolve-ReleaseTool sourcelink } | Should -Throw "*Required tool 'sourcelink' was not found*"
    }

    It 'distinguishes native PDBs from unavailable symbols' {
        $native = Join-Path $TestDrive 'native.pdb'
        Set-Content -LiteralPath $native -Value 'Microsoft C/C++ MSF 7.00'
        (Test-ReleasePdb $valid.Dll $native -CheckMetadataOnly).Result | Should -Be 'unsupported-pdb'
    }

    It 'reports corrupt Portable PDB metadata without ending the sweep silently' {
        $corrupt = Join-Path $TestDrive 'corrupt.pdb'
        [IO.File]::WriteAllBytes($corrupt, [Text.Encoding]::ASCII.GetBytes('BSJB invalid metadata'))
        (Test-ReleasePdb $valid.Dll $corrupt -CheckMetadataOnly).Result | Should -Be 'metadata-invalid'
    }

    It 'checks all library TFMs and explicitly reports expected no-lib packages' {
        $rows = @(Invoke-SourceLinkSweep $packages Original $symbols '' -CheckMetadataOnly)
        @($rows | Where-Object Result -eq 'metadata-valid').Count | Should -Be 2
        @($rows | Where-Object Result -eq 'no-lib-dll').Count | Should -Be 1
        ($rows.Tfm -contains 'net8.0') | Should -Be $true
        ($rows.Tfm -contains 'net10.0') | Should -Be $true
        $rows[0].PackageSha256 | Should -Not -BeNullOrEmpty
        $rows[0].SymbolsPackageSha256 | Should -Not -BeNullOrEmpty
        $rows[0].SourceCommit | Should -Be 'fixture-sha'
    }

    It 'reports missing original symbols as a failure' {
        $missing = Join-Path $TestDrive 'missing'
        New-Item -ItemType Directory -Path $missing | Out-Null
        $rows = @(Invoke-SourceLinkSweep $packages Original $missing '' -CheckMetadataOnly)
        @($rows | Where-Object Result -eq 'symbols-not-found').Count | Should -Be 2
    }

    It 'rejects a symbols archive from a different source commit' {
        $wrongSymbols = Join-Path $TestDrive 'wrong-symbols'
        New-Item -ItemType Directory -Path $wrongSymbols | Out-Null
        $null = New-TestPackage $wrongSymbols Library @('net8.0', 'net10.0') $valid -Symbols -SourceCommit 'other-sha'
        $rows = @(Invoke-SourceLinkSweep $packages Original $wrongSymbols '' -CheckMetadataOnly)
        @($rows | Where-Object Result -eq 'artifact-mismatch').Count | Should -Be 2
    }

    It 'does not misreport download transport/tool failures as missing symbols' {
        Mock Invoke-ReleaseTool { [pscustomobject]@{ ExitCode = 1; Output = 'TLS transport failed' } }
        $rows = @(Invoke-SourceLinkSweep $packages Published '' 'https://example.invalid/')
        @($rows | Where-Object Result -eq 'symbol-download-failed').Count | Should -Be 2
        $rows[0].DownloadOutput | Should -Match 'TLS'
    }

    It 'reports a successful download with no PDB separately from tool failure' {
        Mock Invoke-ReleaseTool { [pscustomobject]@{ ExitCode = 0; Output = 'not found' } }
        $rows = @(Invoke-SourceLinkSweep $packages Published '' 'https://example.invalid/')
        @($rows | Where-Object Result -eq 'symbols-not-found').Count | Should -Be 2
    }

    It 'requires the downloaded PDB to match the DLL identity' {
        Mock Invoke-ReleaseTool {
            $outputDir = $Arguments[[array]::IndexOf($Arguments, '--output') + 1]
            Copy-Item -LiteralPath $other.Pdb -Destination (Join-Path $outputDir 'Fixture.pdb')
            [pscustomobject]@{ ExitCode = 0; Output = 'downloaded' }
        }
        $rows = @(Invoke-SourceLinkSweep $packages Published '' 'https://example.invalid/' -CheckMetadataOnly)
        @($rows | Where-Object Result -eq 'identity-mismatch').Count | Should -Be 2
    }

    It 'performs full published checks for every TFM and retains both tool logs' {
        Mock Invoke-ReleaseTool {
            if ($Tool -eq 'dotnet-symbol') {
                $outputDir = $Arguments[[array]::IndexOf($Arguments, '--output') + 1]
                Copy-Item -LiteralPath $valid.Pdb -Destination (Join-Path $outputDir 'Fixture.pdb')
                return [pscustomobject]@{ ExitCode = 0; Output = 'matching PDB downloaded' }
            }
            [pscustomobject]@{ ExitCode = 0; Output = 'All source documents valid' }
        }
        $rows = @(Invoke-SourceLinkSweep $packages Published '' 'https://example.invalid/')
        @($rows | Where-Object Result -eq 'valid').Count | Should -Be 2
        $rows[0].DownloadOutput | Should -Match 'matching PDB'
        $rows[0].ToolOutput | Should -Match 'All source documents valid'
        $rows[0].MetadataOnly | Should -Be $false
    }

    It 'retains checksum failure output and returns failure' {
        Mock Invoke-ReleaseTool { [pscustomobject]@{ ExitCode = 1; Output = 'source checksum mismatch' } }
        $result = Test-ReleasePdb $valid.Dll $valid.Pdb
        $result.Result | Should -Be 'checksum-mismatch'
        $result.ToolOutput | Should -Match 'checksum mismatch'
    }

    It 'distinguishes unreachable source URLs without claiming a checksum diagnosis' {
        Mock Invoke-ReleaseTool { [pscustomobject]@{ ExitCode = 1; Output = 'Failed to download source: HTTP 404' } }
        (Test-ReleasePdb $valid.Dll $valid.Pdb).Result | Should -Be 'source-url-unreachable'
    }

    It 'preserves unknown tool failures rather than guessing their cause' {
        Mock Invoke-ReleaseTool { [pscustomobject]@{ ExitCode = 1; Output = 'unrecognized diagnostic' } }
        (Test-ReleasePdb $valid.Dll $valid.Pdb).Result | Should -Be 'sourcelink-failed'
    }

    It 'reports full validation only after the source tool succeeds' {
        Mock Invoke-ReleaseTool { [pscustomobject]@{ ExitCode = 0; Output = 'All documents valid' } }
        (Test-ReleasePdb $valid.Dll $valid.Pdb).Result | Should -Be 'valid'
    }

    It 'writes structured evidence and a successful exit for a complete original sweep' {
        $report = Join-Path $TestDrive 'original-report.json'
        & $pwsh -NoProfile -File $helper -PackageDir $packages `
            -Mode Original -SymbolsPackageDir $symbols -ReportPath $report | Out-Null
        $LASTEXITCODE | Should -Be 0
        $json = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $json.Succeeded | Should -Be $true
        $json.Results.Count | Should -Be 3
        $json.MetadataOnly | Should -Be $true
    }

    It 'emits complete structured evidence to stdout when no report path is supplied' {
        $output = & $pwsh -NoProfile -File $helper -PackageDir $packages `
            -Mode Original -SymbolsPackageDir $symbols | Out-String
        $LASTEXITCODE | Should -Be 0
        $json = $output | ConvertFrom-Json
        $json.Results.Count | Should -Be 3
        $json.Results[0].PdbGuid | Should -Be $json.Results[0].DllPdbGuid
    }

    It 'signals failed verification with a nonzero process exit and a retained report' {
        $badPackages = Join-Path $TestDrive 'bad-packages'
        $badSymbols = Join-Path $TestDrive 'bad-symbols'
        New-Item -ItemType Directory -Path $badPackages, $badSymbols | Out-Null
        $null = New-TestPackage $badPackages Broken @('net8.0') $empty
        $null = New-TestPackage $badSymbols Broken @('net8.0') $empty -Symbols
        $report = Join-Path $TestDrive 'failed-report.json'
        & $pwsh -NoProfile -File $helper -PackageDir $badPackages `
            -Mode Original -SymbolsPackageDir $badSymbols -ReportPath $report | Out-Null
        $LASTEXITCODE | Should -Be 1
        (Get-Content -LiteralPath $report -Raw | ConvertFrom-Json).Results[0].Result | Should -Be 'metadata-empty'
    }

    It 'does not mark a limited sweep as complete' {
        $report = Join-Path $TestDrive 'partial-report.json'
        & $pwsh -NoProfile -File $helper -PackageDir $packages `
            -Mode Original -SymbolsPackageDir $symbols -Max 1 -ReportPath $report | Out-Null
        $LASTEXITCODE | Should -Be 1
        (Get-Content -LiteralPath $report -Raw | ConvertFrom-Json).Partial | Should -Be $true
    }

    It 'fails explicitly for an empty package directory' {
        $emptyDir = Join-Path $TestDrive 'empty-packages'
        New-Item -ItemType Directory -Path $emptyDir | Out-Null
        { Invoke-SourceLinkSweep $emptyDir Original $symbols '' } | Should -Throw 'No .nupkg*'
    }

    It 'returns an infrastructure-error exit and structured diagnostics when it cannot run' {
        $report = Join-Path $TestDrive 'error-report.json'
        & $pwsh -NoProfile -File $helper -PackageDir (Join-Path $TestDrive 'absent-folder') `
            -Mode Original -SymbolsPackageDir $symbols -ReportPath $report | Out-Null
        $LASTEXITCODE | Should -Be 2
        $json = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $json.Succeeded | Should -Be $false
        $json.Errors.Count | Should -Be 1
    }

    It 'keeps failure stdout parseable JSON and sends diagnostics to stderr' {
        $stderr = Join-Path $TestDrive 'failure-stderr.txt'
        $output = & $pwsh -NoProfile -File $helper -PackageDir (Join-Path $TestDrive 'absent-folder') `
            -Mode Original -SymbolsPackageDir $symbols 2> $stderr | Out-String
        $LASTEXITCODE | Should -Be 2
        ($output | ConvertFrom-Json).Errors.Count | Should -Be 1
        Get-Content -LiteralPath $stderr -Raw | Should -Match 'Verification could not complete'
    }

    It 'keeps partial-sweep stdout parseable JSON and sends diagnostics to stderr' {
        $stderr = Join-Path $TestDrive 'partial-stderr.txt'
        $output = & $pwsh -NoProfile -File $helper -PackageDir $packages `
            -Mode Original -SymbolsPackageDir $symbols -Max 1 2> $stderr | Out-String
        $LASTEXITCODE | Should -Be 1
        ($output | ConvertFrom-Json).Partial | Should -Be $true
        Get-Content -LiteralPath $stderr -Raw | Should -Match 'Partial sweep'
    }
}
