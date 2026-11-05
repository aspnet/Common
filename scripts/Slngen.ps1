#!/usr/bin/env pwsh

<#
.SYNOPSIS
    The script to generate a custom solution file for a given keyword(s).

.DESCRIPTION
    The script is a wrapper over slngen tool (see https://github.com/microsoft/slngen) to make it easier to build a solution file for a given keyword(s).

.PARAMETER Keywords
    Keywords to search for.
.PARAMETER All
    Include all the projects (except docs). If no Keywords provided, $All is set to true automatically.
.PARAMETER OnlySources
    Include only the source projects.
.PARAMETER IntegrationTests
    Include integration test projects.
.PARAMETER BenchmarkTests
    Include benchmark test projects.
.PARAMETER Docs
    Include conceptual doc projects.
.PARAMETER Folders
    Enables use of folders.
.PARAMETER ExcludePaths
    Exclude paths from search for project files.
.PARAMETER Quiet
    Minimizes console output.
.PARAMETER MsBuildParams
    Parameters passed to MSBuild with slngen.
.PARAMETER RepositoryPath
    Path to the repository
.PARAMETER Help
    Determines whether to show help.

.EXAMPLE
    PS> .\Slngen.ps1 "Polly"
.EXAMPLE
    PS> .\Slngen.ps1 "Polly","Http"
.EXAMPLE
    PS> .\Slngen.ps1 -Folders "Polly","Http"
.EXAMPLE
    PS> .\Slngen.ps1
.EXAMPLE
    PS> .\Slngen.ps1 -NoLaunch
.EXAMPLE
    PS> .\Slngen.ps1 -All -OutSln "myDirectory/MySolution.sln"
.EXAMPLE
    PS> .\Slngen.ps1 -All -ExcludePaths "src\Templates\templates" -OutSln "myDirectory/MySolution.sln"
#>
[System.Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidDefaultValueSwitchParameter', '', Justification="We need it to be turned on by default while still providing a capability to turn it off.")]
param (
    [Parameter(Mandatory = $true, HelpMessage="Keywords to search for.", Position = 0, ParameterSetName = "By Keywords")]
    [string[]]$Keywords,
    [Parameter(Mandatory = $false, HelpMessage="Include all projects (except docs).", ParameterSetName = "All")]
    [switch]$All = $false,
    [Parameter(Mandatory = $false, HelpMessage="Include only source projects.")]
    [switch]$OnlySources = $false,
    [Parameter(Mandatory = $false, HelpMessage="Include integration test projects. Not Compatible with OnlySources parameter")]
    [switch]$IntegrationTests = $true,
    [Parameter(Mandatory = $false, HelpMessage="Include benchmark test projects. Not Compatible with OnlySources parameter")]
    [switch]$BenchmarkTests = $true,
    [Parameter(Mandatory = $false, HelpMessage="Include documentation projects.")]
    [switch]$Docs = $false,
    [Parameter(Mandatory = $false, HelpMessage="Enables use of folders.")]
    [switch]$Folders = $false,
    [Parameter(Mandatory = $false, HelpMessage="Path to exclude from search for project files. Must be repo root folder based.")]
    [string[]]$ExcludePaths = @('src\Tools\MutationTesting\samples\', 'src\ProjectTemplates\**\src\', 'test\**\Snapshots'),
    [Parameter(Mandatory = $false, HelpMessage="Don't launch Visual Studio.")]
    [switch]$NoLaunch = $false,
    [Parameter(Mandatory = $false, HelpMessage="Minimizes console output.")]
    [switch]$Quiet = $false,
    [Parameter(Mandatory = $false, HelpMessage="Output file name.")]
    [string]$OutSln = "SDK.sln",
    [Parameter(Mandatory = $false, HelpMessage="Parameters passed to MSBuild with slngen.")]
    [string[]]$MsBuildParams,
    [Parameter(Mandatory = $false, HelpMessage="Path to the repository.")]
    [string]$RepositoryPath,

    [Parameter(HelpMessage="Show help.")]
    [switch][Alias('h')] $Help
)

# Show help
if ($Help -or ($PSBoundParameters.Count -lt 1)) {
    Get-Help $PSCommandPath
    exit 0;
}

<#
.DESCRIPTION
    This is just to isolate slngen.exe launch command and made it possible to mock and unit test the script.
#>
function Invoke-SlngenExe
{
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        $Folders,
        [Parameter(Mandatory = $true)]
        $OutSln,
        [Parameter(Mandatory = $true)]
        $Globs,
        [Parameter(Mandatory = $true)]
        $NoLaunch,
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        $Exclude,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        $ConsoleOutput,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        $MSBuild
    )

    dotnet tool restore --verbosity minimal | Out-Null
    $process = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @("slngen", "--folders $Folders", "--collapsefolders $Folders", "--ignoreMainProject", "--nologo", "-o $OutSln", "$Globs", "--launch $(!$NoLaunch) $Exclude $ConsoleOutput $MSBuild") `
        -Wait `
        -PassThru `
        -NoNewWindow;

    if ($process.ExitCode -ne 0) {
        throw "Failed to generate the solution."
    }
}

if (!$Keywords) {
    $All = $true
}
else {
    $Docs = $true
}

$InformationPreference = "Continue"

if ([System.IO.Path]::IsPathRooted($OutSln)) {
    $OutSlnPath = $OutSln
}
else {
    $OutSlnPath = Join-Path -Path (Get-Location) -ChildPath $OutSln
}

if (!$RepositoryPath) {
    $RepositoryPath = Split-Path -Parent $PSScriptRoot
}

#This is the list of paths to search for projects when $OnlySources set to $false:
$NonSourcePaths = @("test")
if($BenchmarkTests)
{
    $NonSourcePaths = $NonSourcePaths + "bench"
}
if($IntegrationTests)
{
    $NonSourcePaths = $NonSourcePaths + "int_test"
}

if ($Docs -and (Test-Path ./docs))
{
    $NonSourcePaths = $NonSourcePaths + "docs"
}

Push-Location $RepositoryPath

try {
    [System.Collections.ArrayList]$Globs = @()

    if (!$All) {
        foreach ($Keyword in $Keywords) {
            $Globs += "src/**/*$($Keyword)*/**/*.*sproj"
            if (!$OnlySources) {
                foreach ($NonSourcePath in $NonSourcePaths) {
                    $Globs += $NonSourcePath + "/**/*$($Keyword)*/**/*.*sproj"
                }
            }
        }
    }
    else {
        $Globs += "src/**/*.*sproj"

        if (!$OnlySources) {
            foreach ($NonSourcePath in $NonSourcePaths) {
                $Globs += $NonSourcePath + "/**/*.*sproj"
            }
        }
    }

    $ConsoleOutput = $null
    if ($Quiet) {
        $ConsoleOutput = "--verbosity quiet"
    }

    if ($MsBuildParams) {
        $Joined =  $MsBuildParams -join ';'
        $MSBuild = "--property $Joined"
    } else {
        $MSBuild = $null
    }

    if ($ExcludePaths) {
        #transform arrays from @("path1","path2") into @("--exclude {repository path}\path1", "--exclude {repository path}\path2")
        $Exclude = $ExcludePaths | ForEach-Object { "--exclude $_" }
    } else {
        $Exclude = $null
    }

    # Install required toolset
    . $PSScriptRoot/../eng/common/tools.ps1
    $dotnetRoot = InitializeDotNetCli -install:$true
    Write-Verbose ".NET root: $dotnetRoot"

    Invoke-SlngenExe -Folders $Folders -OutSln "`"$OutSlnPath`"" -Globs $Globs -NoLaunch $NoLaunch -Exclude $Exclude -ConsoleOutput $ConsoleOutput -MSBuild $MSBuild
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
}
