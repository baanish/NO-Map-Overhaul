# Install NO Map Overhaul

NO Map Overhaul is a BepInEx 5 plugin, so installing it means installing BepInEx once, then dropping the mod's folder into it. It isn't in the Nuclear Option Mod Manager catalog yet, so for now it goes in by hand.

Every path below is relative to the game folder, the one holding `NuclearOption.exe`. To open it, right-click Nuclear Option in your Steam library and choose **Manage → Browse local files**.

## 1. Install BepInEx 5

Skip this step if `BepInEx/core` already exists in the game folder: another mod installed it.

1. Download `BepInEx_win_x64_5.4.23.4.zip` from the [BepInEx 5.4.23.4 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.4). That's the version the mod is tested with. Take the `win_x64` file: the game is 64-bit. BepInEx 6 doesn't load BepInEx 5 plugins.
2. Extract the zip into the game folder, so `winhttp.dll`, `doorstop_config.ini`, and the `BepInEx` folder sit beside `NuclearOption.exe`.
3. On Linux through Proton, set the game's Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`, or Proton skips BepInEx.
4. Start the game once and quit at the main menu. BepInEx creates `BepInEx/plugins`, `BepInEx/config`, and `BepInEx/LogOutput.log`.

## 2. Install the mod

1. Close the game.
2. From the [releases page](https://github.com/baanish/NO-Map-Overhaul/releases), download `NoMapOverhaul-v<version>-plugin-only.zip` from the newest release.
3. Extract it into the game folder. Its `BepInEx` folder merges with the one already there, and you end up with:

   ```text
   BepInEx/plugins/NoMapOverhaul/NoMapOverhaul.dll
   BepInEx/plugins/NoMapOverhaul/LICENSE.txt
   BepInEx/plugins/NoMapOverhaul/README.txt
   BepInEx/plugins/NoMapOverhaul/THIRD_PARTY_NOTICES.txt
   ```

4. If `BepInEx/plugins/BaanishUiImprovements` exists, you had this mod under its old name. Follow [Upgrading from Baanish UI Improvements](#upgrading-from-baanish-ui-improvements) too.

## 3. Check that it loaded

Start the game and open `BepInEx/LogOutput.log` in a text editor. This line means the mod loaded:

```text
[Info   :NO Map Overhaul] NO Map Overhaul 0.5.0 loaded.
```

In a mission, your side's runways show as green strips on the minimap, and the full map has a **Tools** button just outside its top-left corner.

If the line is missing, check that `NoMapOverhaul.dll` sits at `BepInEx/plugins/NoMapOverhaul/NoMapOverhaul.dll`, and that `LogOutput.log` changed when you last started the game. An old log means BepInEx itself didn't start: go back to step 1.

## Change settings in game

This part is optional. BepInEx Configuration Manager opens every setting in a window when you press F1 in game, and changes apply at once. Download `BepInEx.ConfigurationManager_BepInEx5_v18.4.1.zip` from its [18.4.1 release](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/tag/v18.4.1), the version the mod is tested with, and extract it into the game folder the same way.

Without it, edit `BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg` while the game is closed. The first start creates that file. The [user guide](USER_GUIDE.md#settings) lists every setting.

## Upgrading from Baanish UI Improvements

Up to 0.4.0 this mod was called Baanish UI Improvements.

1. Close the game.
2. Delete `BepInEx/plugins/BaanishUiImprovements`. If Nuclear Option Mod Manager had switched the mod off, the folder is `BepInEx/disabledPlugins/BaanishUiImprovements` instead.
3. Install NO Map Overhaul as in [step 2](#2-install-the-mod).

Your settings carry over. On the first start, the mod copies everything saved in `BepInEx/config/com.baanish.nuclearoption.uiimprovements.cfg` into its own file and logs a line starting `Carried`. It leaves the old file in place.

The missile arrows are now a mod of their own, [NO Missile Indicators](https://github.com/baanish/NO-Missile-Indicators). Install it too if you want them back. It reads your saved missile arrow settings from the old file.

If you forget to delete the old folder, NO Map Overhaul switches the old plugin off for that session, so nothing draws twice, and logs a warning naming the folder to delete.

## Updating to a new version

Close the game, download the newest `NoMapOverhaul-v<version>-plugin-only.zip`, and extract it into the game folder, replacing the files already there. Your settings stay.

## Uninstalling

Delete the `BepInEx/plugins/NoMapOverhaul` folder and `BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg`. BepInEx can stay for your other mods.

If `BepInEx/config/com.baanish.nuclearoption.uiimprovements.cfg` is still there and you don't use NO Missile Indicators, delete it as well. Otherwise, installing NO Map Overhaul again copies its settings back in.
