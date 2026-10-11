[CmdletBinding()]
param([string[]]$Groups = @('DrawingPeerTests', 'DrawingPeerStateTests', 'DrawingPeerQuarantineTests', 'DrawingDiscoveryTests', 'DrawingReceiveBudgetTests', 'DrawingNetworkScheduleTests', 'DrawingNetworkLogTests', 'SteamConnectionOwnershipTests', 'DrawingProtocolTests', 'ChannelTests', 'CollaborativeEraseTests', 'ReceivedDrawingsTests'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/NoMapOverhaul.Tests'
& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Sharing test compilation failed.' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin/Release/net8.0/NoMapOverhaul.Tests.dll'))
$count = 0
foreach ($name in $Groups) {
    $type = $assembly.GetType("NoMapOverhaul.Tests.$name", $true)
    $tests = $type.GetField('All', [Reflection.BindingFlags]'Static,Public').GetValue($null)
    foreach ($test in $tests) {
        $test.Item2.Invoke()
        Write-Output "PASS $($test.Item1)"
        $count++
    }
}
Write-Output "All $count sharing tests passed."
