# Agent notes

Client-side BepInEx 5 mod for Nuclear Option. The README covers the features, `CONTRIBUTING.md` the build and tests, and `docs/DESIGN.md` how the drawing works.

- **Game code:** decompile the installed `Assembly-CSharp.dll` into `.cache/decompiled/` (gitignored) with `ilspycmd`, and read the real type before relying on it.
- **Installing:** run `pwsh ./build/Build.ps1 -Install` as a background task. It waits for the game to exit before copying, so start it while Aanish is still playing instead of asking him to report that he closed the game. Keep one queued install at a time: a waiting one copies whatever DLL is newest when the game exits, and several racing for the same file fail.
- **Settings:** BepInEx keeps saved values over new defaults. When you change a default that Aanish should see, delete that key from `BepInEx/config/com.baanish.nuclearoption.uiimprovements.cfg` after the game closes. Update the settings table in `docs/USER_GUIDE.md` in the same change.
- **Runway data:** `tools/dump_runways.py <game dir>` prints every airbase's runways offline. It needs UnityPy and TypeTreeGeneratorAPI, installed in `E:/Development/NO-Throttle-Detents/.cache/unitypy-venv`. Painted numbers aren't in the data. Add confirmed mismatches to `RunwayNames.PaintedNumbers`.
- **Verification:** unit tests cover the pure logic (`ApproachSelector`, `RunwayNames`). Everything drawn on screen is checked in game by Aanish from screenshots. Record the result in `docs/TESTING.md`.
- **Releasing:** follow `docs/RELEASING.md`. `pwsh ./build/Release.ps1 -AllowDirty` checks the packages before commit. Publishing (tag, push, GitHub release, NOMNOM pull request) waits for Aanish's go-ahead.
