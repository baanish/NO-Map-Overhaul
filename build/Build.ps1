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

dotnet run --project (Join-Path $root 'tests\NoMapOverhaul.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$project = Join-Path $root 'src\NoMapOverhaul\NoMapOverhaul.csproj'
dotnet build $project -c Release "-p:GameDir=$game"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$dll = Join-Path $root 'src\NoMapOverhaul\bin\Release\netstandard2.1\NoMapOverhaul.dll'
if ($Install) {
    # NOMM toggles a mod by moving its folder between plugins and disabledPlugins; reinstall wherever it is now.
    $target = Join-Path $game 'BepInEx\plugins\NoMapOverhaul'
    $disabled = Join-Path $game 'BepInEx\disabledPlugins\NoMapOverhaul'
    # A first install over a disabled Baanish UI Improvements keeps the mod disabled.
    $oldDisabled = Join-Path $game 'BepInEx\disabledPlugins\BaanishUiImprovements'
    if (-not (Test-Path $target) -and ((Test-Path $disabled) -or (Test-Path $oldDisabled))) { $target = $disabled }
    New-Item -ItemType Directory -Force $target | Out-Null
    $installed = Join-Path $target 'NoMapOverhaul.dll'
    # This mod was Baanish UI Improvements up to 0.4.0; left installed, that plugin would load beside this one.
    $oldInstalls = @('plugins', 'disabledPlugins' | ForEach-Object { Join-Path $game "BepInEx\$_\BaanishUiImprovements" } | Where-Object { Test-Path $_ })
    # The running game locks the DLL; an identical build needs no copy, so metadata can still refresh mid-session.
    $copy = -not (Test-Path $installed) -or (Get-FileHash $installed).Hash -ne (Get-FileHash $dll).Hash
    if ($copy -or $oldInstalls) {
        $gameProcess = Get-Process -Name NuclearOption -ErrorAction SilentlyContinue
        if ($gameProcess) {
            Write-Output 'Waiting for Nuclear Option to close before installing...'
            $gameProcess | Wait-Process
        }
    }
    if ($copy) { Copy-Item $dll $target -Force }
    foreach ($old in $oldInstalls) {
        Remove-Item -LiteralPath $old -Recurse -Force
        Write-Output "Removed the old Baanish UI Improvements install at $old"
    }

    # Local-only NOMM listing (no download URL), so the mod can be toggled in NOMM like the other personal mods.
    $version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Select-Object -First 1
    $hash = (Get-FileHash $installed -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        id       = 'NoMapOverhaul' # matches the planned NOMNOM catalog id
        artifact = [ordered]@{
            fileName          = 'NoMapOverhaul.dll'
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
