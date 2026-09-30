# Releasing

A release is two ZIP archives and a checksum file, published as a GitHub prerelease, then listed in the Nuclear Option Mod Manager catalog. Releases stay prereleases until multiplayer has been tested.

## Before building

1. Set the version in `src/NoMapOverhaul/NoMapOverhaul.csproj` and `Plugin.PluginVersion`. The release script refuses a mismatch.
2. Add a `CHANGELOG.md` entry and `docs/release-notes/v<version>.md`. The script refuses a release without both.
3. Record the in-game checks in [testing](TESTING.md).
4. Commit, and close Nuclear Option.

## Build the packages

From the repository root:

```powershell
pwsh -NoProfile -File ./build/Release.ps1
```

The script refuses a dirty working tree. It runs the tests, does a clean Release build against the installed game, and checks that the assembly version matches and that no game or BepInEx assemblies were copied. It then writes these ignored files:

```text
artifacts/
  NoMapOverhaul-v<version>-nomm.zip
  NoMapOverhaul-v<version>-plugin-only.zip
  SHA256SUMS.txt
  NoMapOverhaul.nomnom.json
```

To check packaging before committing, add `-AllowDirty`. The output then says it came from a dirty tree and must not be published.

The NOMM archive is flat:

```text
NoMapOverhaul.dll
LICENSE.txt
README.txt
THIRD_PARTY_NOTICES.txt
```

The plugin-only archive extracts beside `NuclearOption.exe`:

```text
BepInEx/plugins/NoMapOverhaul/NoMapOverhaul.dll
BepInEx/plugins/NoMapOverhaul/LICENSE.txt
BepInEx/plugins/NoMapOverhaul/README.txt
BepInEx/plugins/NoMapOverhaul/THIRD_PARTY_NOTICES.txt
```

The same inputs always produce the same archive bytes, because entries have fixed order and timestamps.

## Publish the GitHub prerelease

Tag the release commit `v<version>` only after the script passes. Push the commit and tag, then create a prerelease with the release notes as its body and these assets in this order:

1. `NoMapOverhaul-v<version>-nomm.zip`
2. `NoMapOverhaul-v<version>-plugin-only.zip`
3. `SHA256SUMS.txt`

The NOMM archive must be first. The NOMNOM catalog treats the first asset as the package.

The notes link relative to `docs/release-notes/`, and relative links break on a release page. In the body, rewrite each one to an absolute link at the tag, such as `https://github.com/baanish/NO-Map-Overhaul/blob/v<version>/docs/TESTING.md`.

After publishing, read the tag, prerelease flag, asset order, and asset checksums back from GitHub. A successful upload alone doesn't prove the release is right.

## List it in Nuclear Option Mod Manager

NOMM reads its catalog from [NOMNOM](https://github.com/KopterBuzz/NOMNOM). Its [submission rules](https://github.com/KopterBuzz/NOMNOM#how-to-add-your-nuclear-option-mod-to-nomnom) require public source for custom DLLs, a GitHub release, one mod per repository, and a parseable version tag.

For the first listing, fork NOMNOM, add `artifacts/NoMapOverhaul.nomnom.json` as `modManifests/NoMapOverhaul.json`, and open a pull request to `main`. The manifest sets `autoUpdateArtifacts` to `True`, so NOMNOM's hourly updater imports later GitHub releases without another pull request.
