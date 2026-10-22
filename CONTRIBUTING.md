# Contributing

Bug reports and ideas are welcome. Talk to me before starting a larger change, so we can agree on how it should look.

## Build

You need the .NET 8 SDK, Nuclear Option, and BepInEx 5 installed in the game. The build compiles against the game's own assemblies and never copies them into the output.

```powershell
pwsh ./build/Build.ps1            # tests, then a Release build
pwsh ./build/Build.ps1 -Install   # also installs into BepInEx/plugins
```

The script finds the game through Steam. If it can't, pass `-GameDir 'D:\SteamLibrary\steamapps\common\Nuclear Option'` or set `NUCLEAR_OPTION_DIR`.

`-Install` copies the DLL and the shader bundle into `BepInEx/plugins/NoMapOverhaul`. If Nuclear Option Mod Manager has disabled the mod, it copies into `BepInEx/disabledPlugins` instead. It also writes a local `meta.json`, so NOMM lists the mod and can toggle it, and deletes any `BaanishUiImprovements` folder left in either, the mod's install before it was renamed. If the game is running and the DLL changed, the script waits for the game to close before copying.

### The shader bundle

The 3D view's rings are drawn by a shader, `unity/Assets/NoMapOverhaul/WorldRing.shader`, which ships as `nomapoverhaul-shaders.bundle` beside the DLL. The bundle is committed in `unity/Bundles/`, so building the mod needs no Unity. After changing anything in `unity/Assets`, rebuild it with Unity 2022.3.62f2, the game's version, from the Hub:

```powershell
pwsh ./build/Build-Shaders.ps1   # -UnityPath <Unity.exe> if it isn't in the Hub's folder
```

It builds the small Unity project in `unity/` in batch mode, for Direct3D 11 and 12 and Vulkan, and records a hash of the sources beside the bundle. `Build.ps1` and `Release.ps1` stop when the bundle is older than the sources. The project keeps Unity's built-in modules in `Packages/manifest.json`: built without them, the game refuses the bundle as made for an older Unity. Without the bundle the mod still runs and builds the rings on the CPU, and its log says so.

## Test

```powershell
dotnet run --project tests/NoMapOverhaul.Tests -c Release
```

The tests cover the logic that doesn't need Unity: the runway-end choice and naming, the map tools' drawings, undo history, formatting, label and menu layout, 3D ring projection, the perf test's statistics, and settings migration. [Testing](docs/TESTING.md) lists them in full. They don't need the game, and GitHub runs them on every push and pull request. Everything drawn on screen needs an in-game check. Include a screenshot and the map and airbase you tested in your pull request.

Drawing sharing needs a separate Steam account per player, so testing it with three or more players on one PC takes a test build. `pwsh ./build/Build-Loopback.ps1` builds one into `src/NoMapOverhaul/bin/Loopback/`, in which sharing runs over 127.0.0.1 instead of Steam and each game instance shares as its own player. Point a group run's peers at that folder, for example with NO-Agentic-Framework's `group_airborne` mission. Never install or release it: anything on the PC can send it drawings. It compiles in only under the `SharingLoopback` build property, and `build/Release.ps1` refuses to package a DLL that contains it.

[How it works](docs/DESIGN.md) explains the drawing and the game data it reads. To read the game code, decompile `NuclearOption_Data/Managed/Assembly-CSharp.dll` with `ilspycmd` into `.cache/decompiled/`, which Git ignores.

Keep changes focused and explain what they fix.
