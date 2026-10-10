<div align="center">

# FirstMod

**A sandbox and cheat menu for Risk of Rain 2.**
Items, spawns, drones, survivors, teleports and run control in one clean in-game window.

[![Issues](https://img.shields.io/github/issues/KaVoshnik/FirstMod?style=flat-square)](https://github.com/KaVoshnik/FirstMod/issues)
[![Stars](https://img.shields.io/github/stars/KaVoshnik/FirstMod?style=flat-square)](https://github.com/KaVoshnik/FirstMod)
[![Last commit](https://img.shields.io/github/last-commit/KaVoshnik/FirstMod?style=flat-square)](https://github.com/KaVoshnik/FirstMod/commits)
![Languages](https://img.shields.io/badge/UI-RU%20%7C%20EN-8a63d2?style=flat-square)

[Thunderstore](https://thunderstore.io/c/riskofrain2/p/Kavoshnik/FirstMod/) · [Report a bug](https://github.com/KaVoshnik/FirstMod/issues/new?template=bug_report.yml) · [Suggest a feature](https://github.com/KaVoshnik/FirstMod/issues/new?template=feature_request.yml) · [Русская версия](https://github.com/KaVoshnik/FirstMod/blob/main/README.ru.md)

![FirstMod menu](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/menu-world.png)

</div>

## Why FirstMod

- **One window, everything inside.** Press **N** and you get tabs for player, movement, combat, world, items, spawn, players, character and settings.
- **Built for multiplayer.** Local features (flight, noclip, speed, ESP) work for any player. Server features work for everyone as soon as the **host** has the mod.
- **Looks like a tool, not a debug console.** Icon tiles for items and creatures, search, rarity filters, tooltips, RU and EN interface.
- **Safe by default.** Toggles are never saved: cheats do not switch themselves on at launch.

## Screenshots

|                                                                                                             |                                                                                                                 |
| ----------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| ![Items](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/menu-items.png)         | ![Spawn and drones](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/menu-drones.png) |
| **Items:** catalog with icons, rarity filter, search                                                        | **Drones:** add drones as allies, remove your own                                                               |
| ![Character](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/menu-character.png) | ![ESP](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/menu-esp.png)                 |
| **Character:** swap survivor, skin and skills mid-run                                                       | **ESP:** chests, shrines, printers, enemies                                                                     |

## In action

![ESP Demo](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/demo-esp.gif)
![Flight and noclip demo](https://raw.githubusercontent.com/KaVoshnik/FirstMod/main/docs/screenshots/demo-flight.gif)

## Features

| Tab           | What you can do                                                                                                                                                                                                     |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Player**    | God mode, heal / revive, money, lunar coins, free purchases, team level. Target: me / everyone / a specific player.                                                                                                 |
| **Movement**  | Speed, jump height, infinite jumps, flight, noclip, no fall damage, teleport to crosshair / teleporter / nearest chest, loot magnet, chest run.                                                                     |
| **Combat**    | One-shot, attack speed, 100% crit, instant cooldowns, damage / armor / regen / max HP, kill all, kill target at crosshair, freeze enemy AI.                                                                         |
| **World**     | Game speed, charge teleporter, next / restart stage, run timer, director control (stop spawns, credits, start boss), stages cleared, difficulty, artifacts, stage select, portals (blue / gold / celestial / void). |
| **Items**     | Item and equipment catalog with icon tiles, rarity filter, tooltips, search by name and description. Give, remove, clear inventory.                                                                                 |
| **Spawn**     | **Creatures** (enemy or ally, optional elite affix, at the crosshair), **Objects** (chests, shrines, printers...), **Drones** (add as allies, remove your own one by one or all at once).                           |
| **Players**   | Teleport between players, saved teleport points (kept between sessions).                                                                                                                                            |
| **Character** | Swap survivor, skin and skill variants mid-run, save and load item builds.                                                                                                                                          |
| **ESP**       | On-screen labels for chests, shrines, printers and the teleporter (type, price, distance), enemy boxes and HP bars. Local only.                                                                                     |

Game texts (item names, tooltips, stages, survivors...) follow the menu language, so switching the menu to English also gives English item names.

## Quick start

1. Install through **Thunderstore Mod Manager** (recommended), or put `FirstMod.dll` into `BepInEx/plugins/FirstMod/`.
2. Launch the game and press **N** in a run or in the lobby.
3. Playing with friends? The **host** needs the mod for server features.

### Hotkeys (configurable)

| Key     | Action                          |
| ------- | ------------------------------- |
| **N**   | Open / close the menu           |
| **F5**  | God mode                        |
| **F6**  | Flight                          |
| **F7**  | Noclip                          |
| **F8**  | Teleport to crosshair           |
| **F9**  | Kill target at crosshair (host) |
| **F10** | Active features HUD             |
| **F11** | ESP                             |

Settings and hotkeys: `BepInEx/config/com.Kavoshnik.firstmod.cfg`.
Saved builds: `BepInEx/config/FirstMod_builds/`.

### Multiplayer cheat sheet

| Works for any player                                                                                                     | Needs the host to have the mod                                                                                                                                |
| ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Flight, noclip, speed, jump, infinite jumps, ESP, chest run, teleport "to them", ally drones, build presets for yourself | Hostile spawns, objects, run control (difficulty, artifacts, stages), director, god mode / one-shot for others, "bring to me", health / armor / regen changes |

## Troubleshooting

**Controls stop working or the log is full of `MissingFieldException ... jumpWasClaimed`.**
The outdated **SeekersPatcher** mod conflicts with the current game version. Remove it in Thunderstore Mod Manager with _Uninstall SeekersPatcher only_ and do not update or reinstall `bbepis-BepInExPack` while it still requires it.

**Some item icons show only a colored square.**
Some icons can take a few seconds to appear after the menu opens. If they never do, attach `BepInEx/LogOutput.log` to an issue.

**The menu works but a server feature does nothing.**
You are probably a client and the host does not have the mod.

When something else is wrong, the log is the fastest way to a fix: `BepInEx/LogOutput.log` in your profile folder (Thunderstore Mod Manager: _Settings_ -> _Browse profile folder_).

## Feedback and bugs

Found a bug or have an idea? Please open an issue:

- [Report a bug](https://github.com/KaVoshnik/FirstMod/issues/new?template=bug_report.yml)
- [Suggest a feature](https://github.com/KaVoshnik/FirstMod/issues/new?template=feature_request.yml)
- [All issues](https://github.com/KaVoshnik/FirstMod/issues)

A good bug report has: mod version, game version, host or client, steps to reproduce, and the `LogOutput.log` file.

## Recent changes

- **0.10.x**: drone management, wider adaptive window without needless scrollbars, fixed item icons, game texts follow the menu language, "Other" rarity filter for new item tiers.
- **0.9.x**: choose skill variants when swapping survivors, build presets.

## Credits

Made by **Kavoshnik**. Built on [BepInEx](https://github.com/BepInEx/BepInEx), [RoR2BepInExPack](https://thunderstore.io/c/riskofrain2/p/RiskofThunder/RoR2BepInExPack/) and HookGenPatcher.
Risk of Rain 2 is a product of Hopoo Games and Gearbox. This is an unofficial fan mod, not affiliated with them.
