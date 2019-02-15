#requires -version 5
<#
.SYNOPSIS
This script runs a quick check for common errors, such as checking that Visual Studio solutions are up to date or that generated code has been committed to source.
#>
param(
    [switch]$ci
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 1
Import-Module -Scope Local -Force "$PSScriptRoot/common.psm1"

$repoRoot = Resolve-Path "$PSScriptRoot/../../"

[string[]] $errors = @()

function LogError([string]$message) {
    if ($env:TF_BUILD) {
        Write-Host "##vso[task.logissue type=error]$message"
    }
    Write-Host -f Red "error: $message"
    $script:errors += $message
}

try {
    if ($ci) {
        # workaround how this script changes tmp, which messses up 'darc-init.ps1'
        $_originalTmp = $env:TMP
        & $PSScriptRoot\..\common\build.ps1 -ci -prepareMachine -build:$false -restore:$false
        $env:TMP = $_originalTmp
    }

    Write-Host 'Running `darc verify`'

    & "$repoRoot/eng/common/darc-init.ps1"

    try {
        Invoke-Block { & darc verify --verbose }
    }
    catch {
        LogError '`darc verify` failed'
        exit 1
    }

    Write-Host "Checking that solutions are up to date"

    Get-ChildItem "$repoRoot/*.sln" -Recurse `
        | % {
            Write-Host "  Checking $(Split-Path -Leaf $_)"
            $slnDir = Split-Path -Parent $_
            $sln = $_
            & dotnet sln $_ list `
                | ? { $_ -ne 'Project(s)' -and $_ -ne '----------' } `
                | % {
                        $proj = Join-Path $slnDir $_
                        if (-not (Test-Path $proj)) {
                            LogError "Missing project. Solution references a project which does not exist: $proj. [$sln] "
                        }
                    }
        }

    #
    # Generated code check
    #

    Write-Host "Re-running code generation"

    Write-Host "Re-generating project lists"
    Invoke-Block {
        & $PSScriptRoot\GenerateProjectList.ps1
    }

    Write-Host "Re-generating references assemblies"
    Invoke-Block {
        & $PSScriptRoot\GenerateReferenceAssemblies.ps1
    }

    Write-Host "Re-generating package baselines"
    $dotnet = 'dotnet'
    if ($ci) {
        $dotnet = "$repoRoot/.dotnet/dotnet.exe"
    }
    Invoke-Block {
        & $dotnet run -p "$repoRoot/eng/tools/BaselineGenerator/"
    }

    Write-Host "git diff"
    & git diff --ignore-space-at-eol --exit-code
    if ($LastExitCode -ne 0) {
        $status = git status -s | Out-String
        $status = $status -replace "`n","`n    "
        LogError "Generated code is not up to date. You might need to regenerate the reference assemblies or project list (see docs/ReferenceAssemblies.md and docs/ReferenceResolution.md)"
    }
}
finally {
    Write-Host ""
    Write-Host "Summary:"
    Write-Host ""
    Write-Host "   $($errors.Length) error(s)"
    Write-Host ""

    foreach ($err in $errors) {
        Write-Host -f Red "error : $err"
    }

    if ($errors) {
        exit 1
    }
}
