param (
    [string]$OutputPath,
    [string]$Version = $null,
    [switch]$IncludeTestPackage = $false
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param (
        [string]$Command,
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Command $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Remove-BuildDirectory {
    param (
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    if (Test-Path -LiteralPath $Path) {
        throw "Failed to remove build directory '$Path'."
    }
}

# if version is not set, then run a script to get it
if ($Version -eq "")
{
    $VSIXPackageVersion = Get-Content $PSScriptRoot/VSIXPackageVersion.json | ConvertFrom-Json
    $Version = $VSIXPackageVersion.PackageVersion
}

$PackageVersion = $Version
$OverrideJson = join-path $PSScriptRoot "override.json"

if ($null -eq $PackageVersion)
{
    Write-Error "Version not set"
    exit 1
}

Write-Host "Using version $PackageVersion"

$TaskPath = Join-Path $PSScriptRoot "tasks/PublishAIEvaluationReport"
Remove-BuildDirectory (Join-Path $TaskPath "node_modules")
Remove-BuildDirectory (Join-Path $TaskPath "dist")
Remove-BuildDirectory (Join-Path $PSScriptRoot "node_modules")
Remove-BuildDirectory (Join-Path $PSScriptRoot "dist")

# Write-Information "Building Report Publishing task"
Set-Location $TaskPath
Invoke-NativeCommand "npm" @("ci", "--omit=dev")
# Copy task files to dist folder
New-Item -ItemType Directory -Path ./dist -Force
copy-item -Path ./task.json -Destination ./dist/ -Force
copy-item -Path ./index.js -Destination ./dist/ -Force
copy-item -Path ./package.json -Destination ./dist/ -Force
copy-item -Path ./node_modules -Destination ./dist/node_modules -Force -Recurse

# remove the test files from resolve package because they are currently breaking vsix signing (zero length)
remove-item -Path ./dist/node_modules/resolve/test -Recurse -Force -ErrorAction SilentlyContinue

@{  version = $PackageVersion
    public = $true } | ConvertTo-Json -Compress | Out-File -FilePath $OverrideJson -Encoding ascii
    
# Write-Information "Building Extension Package" 
Set-Location $PSScriptRoot
Invoke-NativeCommand "npm" @("ci")
Invoke-NativeCommand "npx" @("tsc", "-b")
Invoke-NativeCommand "npx" @("vite", "build")
    
# Copy LICENSE file from the root
copy-item -Path $PSScriptRoot/../../../../../LICENSE -Destination . -Force

Invoke-NativeCommand "npx" @("tfx-cli", "extension", "create", "--overrides-file", $OverrideJson, "--output-path", $OutputPath)

if ($true -eq $IncludeTestPackage) {
    @{  version = $PackageVersion
        id = "microsoft-extensions-ai-evaluation-report-test" 
        name = "[TEST] Azure DevOps AI Evaluation Report" } | ConvertTo-Json -Compress | Out-File -FilePath $OverrideJson -Encoding ascii

    # Build Preview version of the extension for testing
    Invoke-NativeCommand "npx" @("tfx-cli", "extension", "create", "--overrides-file", $OverrideJson, "--output-path", $OutputPath)
}
