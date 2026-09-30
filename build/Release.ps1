[CmdletBinding()]
param(
    [string]$GameDir,
    # Package a dirty working tree for a local check. The output is labelled as not releasable.
    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $root 'src\BaanishUiImprovements\BaanishUiImprovements.csproj'
$tests = Join-Path $root 'tests\BaanishUiImprovements.Tests'
$pluginSource = Join-Path $root 'src\BaanishUiImprovements\Plugin.cs'
$artifacts = Join-Path $root 'artifacts'
$staging = Join-Path $artifacts 'release-staging'
$nommStage = Join-Path $staging 'nomm'
$pluginStage = Join-Path $staging 'plugin-only'
$safeRoot = $root.Replace('\', '/')

$gitStatus = @(& git -c "safe.directory=$safeRoot" status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the Git working-tree status.' }
if ($gitStatus.Count -ne 0 -and -not $AllowDirty) {
    throw 'The release must be built from a clean Git working tree. Pass -AllowDirty for a local packaging check.'
}

[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid project version '$version'." }

$pluginVersion = [regex]::Match([IO.File]::ReadAllText($pluginSource), 'const\s+string\s+PluginVersion\s*=\s*"(?<version>[^"]+)"')
if (-not $pluginVersion.Success) { throw 'Plugin.PluginVersion was not found.' }
if ($pluginVersion.Groups['version'].Value -ne $version) {
    throw "Plugin.PluginVersion '$($pluginVersion.Groups['version'].Value)' does not match project version '$version'."
}
if (-not (Select-String -LiteralPath (Join-Path $root 'CHANGELOG.md') -Pattern "^## $([regex]::Escape($version))\b" -Quiet)) {
    throw "CHANGELOG.md has no entry for $version."
}
if (-not (Test-Path -LiteralPath (Join-Path $root "docs\release-notes\v$version.md"))) {
    throw "docs/release-notes/v$version.md is missing."
}

$game = & (Join-Path $PSScriptRoot 'Find-NuclearOption.ps1') -GameDir $GameDir

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Copy-PackageFiles([string]$Destination, [string]$PluginDll) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -LiteralPath $PluginDll -Destination (Join-Path $Destination 'BaanishUiImprovements.dll')
    $readme = [IO.File]::ReadAllText((Join-Path $root 'packaging\README.txt'))
    if ($readme -notlike '*@VERSION@*') { throw 'packaging/README.txt does not contain its @VERSION@ token.' }
    [IO.File]::WriteAllText((Join-Path $Destination 'README.txt'), $readme.Replace('@VERSION@', $version), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $Destination 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $Destination 'THIRD_PARTY_NOTICES.txt')
}

# Fixed entry order and timestamps, so the same inputs always produce the same archive bytes.
function New-DeterministicZip([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\', '/') + '\'
        $timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File | Sort-Object FullName) {
            $entry = $archive.CreateEntry($file.FullName.Substring($sourceRoot.Length).Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $timestamp
            $input = [IO.File]::OpenRead($file.FullName)
            try {
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            }
            finally { $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function Assert-Package([string]$Path, [string[]]$ExpectedEntries) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $actual = @($archive.Entries | Where-Object Name | ForEach-Object FullName | Sort-Object)
        $readme = $archive.Entries | Where-Object { $_.Name -eq 'README.txt' } | Select-Object -First 1
        $reader = [IO.StreamReader]::new($readme.Open(), [Text.Encoding]::UTF8, $true)
        try {
            if ($reader.ReadToEnd() -match '@VERSION@') { throw "Package '$Path' contains an unresolved version token." }
        }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }

    if (Compare-Object ($ExpectedEntries | Sort-Object) $actual) {
        throw "Package '$Path' has unexpected contents: $($actual -join ', ')"
    }
}

Invoke-DotNet @('run', '--project', $tests, '-c', 'Release')
Invoke-DotNet @('clean', $project, '-c', 'Release', '--nologo', '--verbosity', 'minimal', "-p:GameDir=$game")
Invoke-DotNet @('build', $project, '-c', 'Release', '--nologo', '--verbosity', 'minimal', "-p:GameDir=$game")

$pluginDll = Join-Path $root 'src\BaanishUiImprovements\bin\Release\netstandard2.1\BaanishUiImprovements.dll'
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($pluginDll).Version.ToString(3)
if ($assemblyVersion -ne $version) { throw "Assembly version '$assemblyVersion' does not match project version '$version'." }
$builtFiles = @(Get-ChildItem -LiteralPath (Split-Path $pluginDll) -Filter *.dll | ForEach-Object Name)
if ($builtFiles.Count -ne 1) { throw "The build output contains copied assemblies: $($builtFiles -join ', ')" }

if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $nommStage, $pluginStage | Out-Null
Copy-PackageFiles $nommStage $pluginDll
Copy-PackageFiles (Join-Path $pluginStage 'BepInEx\plugins\BaanishUiImprovements') $pluginDll

$nommZip = Join-Path $artifacts "BaanishUiImprovements-v$version-nomm.zip"
$pluginZip = Join-Path $artifacts "BaanishUiImprovements-v$version-plugin-only.zip"
New-DeterministicZip $nommStage $nommZip
New-DeterministicZip $pluginStage $pluginZip
Remove-Item -LiteralPath $staging -Recurse -Force

Assert-Package $nommZip @('BaanishUiImprovements.dll', 'LICENSE.txt', 'README.txt', 'THIRD_PARTY_NOTICES.txt')
Assert-Package $pluginZip @(
    'BepInEx/plugins/BaanishUiImprovements/BaanishUiImprovements.dll',
    'BepInEx/plugins/BaanishUiImprovements/LICENSE.txt',
    'BepInEx/plugins/BaanishUiImprovements/README.txt',
    'BepInEx/plugins/BaanishUiImprovements/THIRD_PARTY_NOTICES.txt')

$checksums = @($nommZip, $pluginZip | ForEach-Object { "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path -Leaf $_)" })
[IO.File]::WriteAllLines((Join-Path $artifacts 'SHA256SUMS.txt'), [string[]]$checksums, [Text.Encoding]::ASCII)

# The catalog listing for NOMNOM, filled in with this build's version and NOMM archive hash.
$nommHash = (Get-FileHash -LiteralPath $nommZip -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [IO.File]::ReadAllText((Join-Path $root 'packaging\NOMNOM-MANIFEST.template.json')).Replace('@VERSION@', $version).Replace('@SHA256@', $nommHash)
[IO.File]::WriteAllText((Join-Path $artifacts 'BaanishUiImprovements.nomnom.json'), $manifest, [Text.UTF8Encoding]::new($false))

$state = if ($gitStatus.Count -eq 0) { "from commit $((& git -c "safe.directory=$safeRoot" rev-parse --short HEAD).Trim())" } else { 'from a DIRTY tree (not releasable)' }
Write-Host "v$version $state passed tests, build, and package validation."
Get-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt')
