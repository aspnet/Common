#!/usr/bin/env pwsh
<#
.DESCRIPTION
    Creates API baseline files representing the current API surface exposed by this repo.
#>

if ($PSVersionTable.PSVersion.Major -lt 6) {
    Write-Host "PowerShell 6.0 or greater is required to run this script. See https://aka.ms/install-powershell."
    Write-Host "Current version:" $PSVersionTable.PSVersion.ToString()
    exit
}

Write-Output "Installing required toolset"

. $PSScriptRoot/../eng/common/tools.ps1
InitializeDotNetCli -install $true | Out-Null

$Project = $PSScriptRoot + "/../eng/Tools/ApiChief/ApiChief.csproj"
$Command = $PSScriptRoot + "/../artifacts/bin/ApiChief/Debug/net11.0/ApiChief.dll"
$LibrariesFolder = $PSScriptRoot + "/../src/Libraries"

Write-Output "Building ApiChief tool"

dotnet build $Project --nologo --verbosity q

Write-Output "Creating API baseline files in the src/Libraries folder"

Get-ChildItem -Path $LibrariesFolder -Depth 1 -Include *.csproj | ForEach-Object `
{
    $name = Split-Path $_.FullName -LeafBase
    $path = "$PSScriptRoot\..\artifacts\bin\$name\Debug\net11.0\$name.dll"
    Write-Host "  Processing" $name
    dotnet $Command $path emit baseline -o "$LibrariesFolder/$name/$name.json"
}
