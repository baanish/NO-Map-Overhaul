# Contributing

Bug reports and ideas are welcome. Talk to me before starting a larger change, so we can agree on how it should look.

## Build

You need the .NET 8 SDK, Nuclear Option, and BepInEx 5 installed in the game. The build compiles against the game's own assemblies and never copies them into the output.

```powershell
pwsh ./build/Build.ps1            # tests, then a Release build
pwsh ./build/Build.ps1 -Install   # also installs into BepInEx/plugins
```

The script finds the game through Steam. If it can't, pass `-GameDir 'D:\SteamLibrary\steamapps\common\Nuclear Option'` or set `NUCLEAR_OPTION_DIR`.

`-Install` copies the DLL into `BepInEx/plugins/BaanishUiImprovements`. If Nuclear Option Mod Manager has disabled the mod, it copies into `BepInEx/disabledPlugins` instead. It also writes a local `meta.json`, so NOMM lists the mod and can toggle it. If the game is running and the DLL changed, the script waits for the game to close before copying.

## Test

```powershell
dotnet run --project tests/BaanishUiImprovements.Tests -c Release
```

The tests cover the runway-end choice and the naming rules. They don't need the game, and GitHub runs them on every push. Everything drawn on screen needs an in-game check. Include a screenshot and the map and airbase you tested in your pull request.

[How it works](docs/DESIGN.md) explains the drawing and the game data it reads. To read the game code, decompile `NuclearOption_Data/Managed/Assembly-CSharp.dll` with `ilspycmd` into `.cache/decompiled/`, which Git ignores.

Keep changes focused and explain what they fix.
