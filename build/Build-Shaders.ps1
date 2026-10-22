[CmdletBinding()]
param(
    # Unity.exe of the version unity/ProjectSettings/ProjectVersion.txt names, if the Hub's install isn't found.
    [string]$UnityPath,
    # Only check that the committed bundle was built from the shader sources as they are now.
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'unity'
$bundle = Join-Path $project 'Bundles\nomapoverhaul-shaders.bundle'
$stamp = "$bundle.sources"

# The bundle is committed, so a build needs no Unity. Beside it is the hash of the sources it was built from.
function Get-SourceHash {
    $files = Get-ChildItem -LiteralPath (Join-Path $project 'Assets') -Recurse -File -Include *.shader, *.cginc, *.cs | Sort-Object FullName
    $text = ($files | ForEach-Object { $_.FullName.Substring($project.Length + 1).Replace('\', '/') + "`n" + [IO.File]::ReadAllText($_.FullName).Replace("`r`n", "`n") }) -join "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

if ($Check) {
    if (-not (Test-Path -LiteralPath $bundle) -or -not (Test-Path -LiteralPath $stamp) -or
        (Get-Content -LiteralPath $stamp -Raw).Trim() -ne (Get-SourceHash)) {
        throw 'unity/Bundles/nomapoverhaul-shaders.bundle is missing or older than the shaders in unity/Assets. Rebuild it with build/Build-Shaders.ps1.'
    }
    return
}

$version = (Select-String -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion: (\S+)$').Matches[0].Groups[1].Value
$editor = if ($UnityPath) { $UnityPath } else { Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (-not (Test-Path -LiteralPath $editor)) { throw "Unity $version isn't at $editor. Pass its Unity.exe with -UnityPath." }

$logFolder = Join-Path $root '.cache'
New-Item -ItemType Directory -Force $logFolder | Out-Null
$log = Join-Path $logFolder 'unity-shaders.log'
$arguments = @('-batchmode', '-quit', '-nographics', '-buildTarget', 'Win64', '-projectPath', "`"$project`"",
    '-executeMethod', 'NoMapOverhaul.Editor.ShaderBundleBuild.Build', '-logFile', "`"$log`"")
$unity = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$unity.WaitForExit()
if ($unity.ExitCode -ne 0) { throw "Unity exited with code $($unity.ExitCode). See $log" }
if (-not (Select-String -LiteralPath $log -Pattern '^\[NoMapOverhaul\] Shader bundle built' -Quiet)) { throw "Unity didn't build the bundle. See $log" }

[IO.File]::WriteAllText($stamp, (Get-SourceHash) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Built $bundle"
