# Third-party notices

## NOAutopilot

The only code adapted from NOAutopilot is the Waypoint tool's rule for when a waypoint counts as reached or passed, and its two default distances, 2.5 km and 10 km (`RouteProgress.IsReached` in `src/NoMapOverhaul/MapTools/Waypoint/RouteProgress.cs`).

Separately, when NOAutopilot is installed, the mod patches NOAutopilot's right-click handler at runtime so that it skips right clicks while the map tools rail is open (`src/NoMapOverhaul/MapTools/AutopilotRightClickPatch.cs`). The patch finds the handler by name. NOAutopilot isn't bundled, and none of its code is copied for the patch.

```text
MIT License

Copyright (c) 2026 qwerty1423 and NOAutopilot contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
