"""Renames the mod from Baanish UI Improvements to NO Map Overhaul: folders, project files, namespaces, the plugin's
GUID and name, script paths, package names, the NOMNOM manifest, and doc links.

Run from anywhere: python tools/rename_to_map_overhaul.py. It rewrites tracked files in place and moves folders with
git mv, so `git diff` shows the result. Rerunning it changes nothing.

Left alone on purpose: docs/release-notes/ and CHANGELOG.md (history), and the 0.4.0 settings fixture (a real file the
old plugin wrote). Code that must name the old plugin, to carry settings over, is written by hand after this runs.
"""

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

MOVES = [
    ("src/BaanishUiImprovements", "src/NoMapOverhaul"),
    ("src/NoMapOverhaul/BaanishUiImprovements.csproj", "src/NoMapOverhaul/NoMapOverhaul.csproj"),
    ("tests/BaanishUiImprovements.Tests", "tests/NoMapOverhaul.Tests"),
    ("tests/NoMapOverhaul.Tests/BaanishUiImprovements.Tests.csproj", "tests/NoMapOverhaul.Tests/NoMapOverhaul.Tests.csproj"),
]

# Applied in order, longest and most specific first.
REPLACEMENTS = [
    ("https://github.com/baanish/baanish-ui-improvements", "https://github.com/baanish/NO-Map-Overhaul"),
    ('"githubRepoName": "baanish-ui-improvements"', '"githubRepoName": "NO-Map-Overhaul"'),
    ("# baanish-ui-improvements", "# NO Map Overhaul"),
    ("com.baanish.nuclearoption.uiimprovements", "com.baanish.nuclearoption.mapoverhaul"),
    ("Baanish UI Improvements", "NO Map Overhaul"),
    ("BaanishUiImprovements", "NoMapOverhaul"),
]

SKIPPED = (
    "docs/release-notes/",
    "CHANGELOG.md",
    "tests/NoMapOverhaul.Tests/Fixtures/",
    "tools/rename_to_map_overhaul.py",
)

TEXT_SUFFIXES = {".cs", ".csproj", ".ps1", ".py", ".md", ".txt", ".json", ".yml", ".cfg", ".gitignore", ".gitattributes"}


def git(*args: str) -> str:
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True, text=True).stdout


def main() -> int:
    for source, target in MOVES:
        if (ROOT / source).exists() and not (ROOT / target).exists():
            git("mv", source, target)
            print(f"moved {source} -> {target}")

    for name in git("ls-files").splitlines():
        path = ROOT / name
        if name.startswith(SKIPPED) or not path.is_file() or (path.suffix or path.name) not in TEXT_SUFFIXES:
            continue

        text = path.read_bytes().decode("utf-8")  # bytes, so line endings and any BOM stay as they are
        renamed = text
        for old, new in REPLACEMENTS:
            renamed = renamed.replace(old, new)

        if renamed != text:
            path.write_bytes(renamed.encode("utf-8"))
            print(f"rewrote {name}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
