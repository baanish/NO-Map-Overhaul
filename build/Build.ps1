[CmdletBinding()]
param(
    [string]$GameDir,
    # Copy the built plugin into the game's BepInEx/plugins folder. The game must be closed.
    [switch]$Install
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$game = & (Join-Path $PSScriptRoot 'Find-NuclearOption.ps1') -GameDir $GameDir

dotnet run --project (Join-Path $root 'tests\BaanishUiImprovements.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$project = Join-Path $root 'src\BaanishUiImprovements\BaanishUiImprovements.csproj'
dotnet build $project -c Release "-p:GameDir=$game"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$dll = Join-Path $root 'src\BaanishUiImprovements\bin\Release\netstandard2.1\BaanishUiImprovements.dll'
if ($Install) {
    # NOMM toggles a mod by moving its folder between plugins and disabledPlugins; reinstall wherever it is now.
    $target = Join-Path $game 'BepInEx\plugins\BaanishUiImprovements'
    $disabled = Join-Path $game 'BepInEx\disabledPlugins\BaanishUiImprovements'
    if (-not (Test-Path $target) -and (Test-Path $disabled)) { $target = $disabled }
    New-Item -ItemType Directory -Force $target | Out-Null
    $installed = Join-Path $target 'BaanishUiImprovements.dll'
    # The running game locks the DLL; an identical build needs no copy, so metadata can still refresh mid-session.
    if (-not (Test-Path $installed) -or (Get-FileHash $installed).Hash -ne (Get-FileHash $dll).Hash) {
        $gameProcess = Get-Process -Name NuclearOption -ErrorAction SilentlyContinue
        if ($gameProcess) {
            Write-Output 'Waiting for Nuclear Option to close before installing...'
            $gameProcess | Wait-Process
        }
        Copy-Item $dll $target -Force
    }

    # Local-only NOMM listing (no download URL), so the mod can be toggled in NOMM like the other personal mods.
    $version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Select-Object -First 1
    $hash = (Get-FileHash $installed -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        id       = 'BaanishUiImprovements' # matches the planned NOMNOM catalog id
        artifact = [ordered]@{
            fileName          = 'BaanishUiImprovements.dll'
            version           = $version
            category          = 'preRelease'
            type              = 'plugin'
            gameVersion       = '0.34.2'
            downloadUrl       = ''
            hash              = "sha256:$hash"
            dependencies      = @()
            incompatibilities = @()
        }
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $target 'meta.json') -Encoding utf8NoBOM
    Write-Output "Installed $version to $target"
} else {
    Write-Output "Built $dll"
}
