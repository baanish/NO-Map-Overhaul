# Agent notes

Client-side BepInEx 5 mod for Nuclear Option. The README covers the features, `CONTRIBUTING.md` the build and tests, and `docs/DESIGN.md` how the drawing works.

- **Game code:** decompile the installed `Assembly-CSharp.dll` into `.cache/decompiled/` (gitignored) with `ilspycmd`, and read the real type before relying on it.
- **Installing:** run `pwsh ./build/Build.ps1 -Install` as a background task. It waits for the game to exit before copying, so start it while Aanish is still playing instead of asking him to report that he closed the game. Keep one queued install at a time: a waiting one copies whatever DLL is newest when the game exits, and several racing for the same file fail.
- **Settings:** BepInEx keeps saved values over new defaults. When you change a default that Aanish should see, delete that key from `BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg` after the game closes. Update the settings table in `docs/USER_GUIDE.md` in the same change.
- **Runway data:** `tools/dump_runways.py <game dir>` prints every airbase's runways offline. It needs UnityPy and TypeTreeGeneratorAPI, installed in `E:/Development/NO-Throttle-Detents/.cache/unitypy-venv`. Painted numbers aren't in the data. Add confirmed mismatches to `RunwayNames.PaintedNumbers`.
- **Map UI data:** `tools/dump_map_ui.py <game dir>` prints the full map's canvas and the game's HUD around it offline, with the same UnityPy venv. Rerun it after a game update and check `MenuLayout.HudBoxes` against it.
- **Verification:** unit tests cover the Unity-free logic (runway choice and names, missile arrow placement, the map tools' store, formatting, and layout, ring projection, perf statistics, settings migration). Everything drawn on screen is checked in game by Aanish from screenshots. Record the result in `docs/TESTING.md`, and update its test count and coverage list when tests change.
- **Releasing:** follow `docs/RELEASING.md`. `pwsh ./build/Release.ps1 -AllowDirty` checks the packages before commit. Publishing (tag, push, GitHub release, NOMNOM pull request) waits for Aanish's go-ahead.
