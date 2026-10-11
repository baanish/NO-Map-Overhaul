[CmdletBinding()]
param([string]$GameDir)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$game = & (Join-Path $root 'build/Find-NuclearOption.ps1') -GameDir $GameDir
$managed = Join-Path $game 'NuclearOption_Data/Managed'
$pluginPath = Join-Path $root 'src/NoMapOverhaul/bin/Release/netstandard2.1/NoMapOverhaul.dll'
if (-not (Test-Path -LiteralPath $pluginPath)) { throw 'Build NoMapOverhaul before running the offline ownership check.' }

# Check installed field bindings and actual prefixes without Harmony detours or native Steam calls.
foreach ($name in @('com.rlabrecque.steamworks.net', 'Mirage.SocketLayer', 'Mirage.SteamworksSocket')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $managed "$name.dll")) | Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $game 'BepInEx/core/BepInEx.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $game 'BepInEx/core/0Harmony.dll')) | Out-Null
$plugin = [Reflection.Assembly]::LoadFrom($pluginPath)
$patch = $plugin.GetType('NoMapOverhaul.Sharing.SteamConnectionOwnershipPatch', $true)
$staticPrivate = [Reflection.BindingFlags]'Static,NonPublic'
$instancePrivate = [Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
$callbackType = [Steamworks.SteamNetConnectionStatusChangedCallback_t]
$serverType = [Mirage.SteamworksSocket.Server]
$clientType = [Mirage.SteamworksSocket.Client]
foreach ($type in @($serverType, $clientType)) {
    $handler = $type.GetMethod('OnConnectionStatusChanged', $instancePrivate, $null, [Type[]]@($callbackType), $null)
    if ($null -eq $handler -or $handler.ReturnType -ne [void]) { throw "Unexpected callback signature on $type" }
}
foreach ($binding in @(
    @($serverType, 'listenSocket', [Steamworks.HSteamListenSocket]),
    @($serverType, 'connections', [Collections.Generic.Dictionary[Steamworks.HSteamNetConnection,Mirage.SteamworksSocket.SteamConnection]]),
    @($clientType, 'connection', [Mirage.SteamworksSocket.SteamConnection])
)) {
    $field = $binding[0].GetField($binding[1], $instancePrivate)
    if ($null -eq $field -or $field.FieldType -ne $binding[2]) { throw "Unexpected Harmony field binding $($binding[0]).$($binding[1])" }
}
$serverPrefix = $patch.GetMethod('ServerOwnsCallback', $staticPrivate)
$clientPrefix = $patch.GetMethod('ClientOwnsCallback', $staticPrivate)
if ($null -eq $serverPrefix -or $null -eq $clientPrefix) { throw 'Built plugin has no ownership prefixes.' }
$connections = [Collections.Generic.Dictionary[Steamworks.HSteamNetConnection,Mirage.SteamworksSocket.SteamConnection]]::new()
$ownConnection = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([Mirage.SteamworksSocket.SteamConnection])
$ownConnection.ConnId = [Steamworks.HSteamNetConnection]::new(10)
$connections.Add($ownConnection.ConnId, $ownConnection)
$listen = [Steamworks.HSteamListenSocket]::new(20)
$count = 0
foreach ($case in @(
    @('owned incoming', 10, 20, 'Connecting', $true, $true),
    @('foreign outgoing to the same peer', 11, 0, 'Connecting', $false, $false),
    @('foreign incoming', 12, 21, 'Connected', $false, $false),
    @('foreign terminal', 11, 0, 'ProblemDetectedLocally', $false, $false),
    @('owned terminal without listener metadata', 10, 0, 'ClosedByPeer', $true, $true),
    @('own listener terminal before registration', 13, 20, 'ClosedByPeer', $true, $false),
    @('invalid connection', 0, 20, 'Connecting', $false, $false)
)) {
    $callback = [Steamworks.SteamNetConnectionStatusChangedCallback_t]::new()
    $callback.m_hConn = [Steamworks.HSteamNetConnection]::new($case[1])
    $info = $callback.m_info
    $info.m_hListenSocket = [Steamworks.HSteamListenSocket]::new($case[2])
    $info.m_eState = [Enum]::Parse([Steamworks.ESteamNetworkingConnectionState], "k_ESteamNetworkingConnectionState_$($case[3])")
    $callback.m_info = $info
    $serverAllowed = $serverPrefix.Invoke($null, [object[]]@($callback, $listen, $connections))
    $clientAllowed = $clientPrefix.Invoke($null, [object[]]@($callback, $ownConnection))
    if ($serverAllowed -ne $case[4] -or $clientAllowed -ne $case[5]) { throw "Incorrect ownership for $($case[0])" }
    Write-Output "PASS offline prefix $($case[0])"
    $count++
}
Write-Output "Installed callback shapes and all $count offline prefix cases passed. Mono Harmony application still requires in-game verification."
