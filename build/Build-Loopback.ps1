# Builds the plugin with the loopback sharing transport, for multiplayer tests of several game instances on one PC
# (an NO-Agentic-Framework group run). Each instance shares as a separate player over 127.0.0.1. Never install or
# release this build: its sharing isn't authenticated.
[CmdletBinding()]
param([string]$GameDir)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$game = & (Join-Path $PSScriptRoot 'Find-NuclearOption.ps1') -GameDir $GameDir
$project = Join-Path $root 'src\NoMapOverhaul\NoMapOverhaul.csproj'
dotnet build $project -c Release "-p:GameDir=$game" -p:SharingLoopback=true `
    "-p:OutputPath=$root\src\NoMapOverhaul\bin\Loopback\" "-p:IntermediateOutputPath=$root\src\NoMapOverhaul\obj\Loopback\"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $root\src\NoMapOverhaul\bin\Loopback\NoMapOverhaul.dll"
