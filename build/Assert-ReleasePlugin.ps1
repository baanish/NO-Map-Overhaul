function Assert-ReleasePlugin([string]$PluginDll) {
    # The loopback sharing transport takes unauthenticated drawings from anything on the PC.
    if ([IO.File]::ReadAllText($PluginDll).Contains('LoopbackDrawingTransport')) {
        throw "$PluginDll contains the loopback sharing transport, which is for tests only. Build with build/Build.ps1."
    }
}
