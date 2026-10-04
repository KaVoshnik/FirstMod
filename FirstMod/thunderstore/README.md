# FirstMod

Cheat / sandbox menu for Risk of Rain 2. Open with **N** (configurable). Russian and English UI.
Server-side features work in multiplayer when the mod is installed on the host.

## Features
- **Player**: god mode, heal / revive, money, lunar coins, free purchases, team level. Target: me / everyone / a specific player.
- **Movement**: speed, jump height, infinite jumps, flight, noclip, no fall damage, teleport to crosshair / teleporter / nearest chest, loot magnet.
- **Combat**: one-shot, attack speed, 100% crit, instant cooldowns, damage / armor / regen / max HP, kill all, kill target at crosshair, freeze enemy AI.
- **World**: game speed, charge teleporter, next / restart stage, run timer, director control (stop spawns, credits, start boss), stages cleared, difficulty, artifacts, stage select, portals (blue / gold / celestial / void), ESP.
- **ESP**: on-screen labels for chests, shrines, printers and the teleporter (type, price, distance), enemy boxes and HP bars. Local only.
- **Chest run**: (Movement tab) walks the nearest chests, opens them and grabs the drops. Works for clients too.
- **Items**: item and equipment catalog with icon tiles, rarity filter, tooltips, search by name and description.
- **Spawn**: creatures (enemy or ally, optional elite affix, at the crosshair) and interactables (chests, shrines, printers...).
- **Players**: teleport between players, saved teleport points (kept between sessions).
- **Character**: swap survivor, skin and skill variants mid-run (console: `fm_body <body> <skin> <target> [variants]`), save / load item builds.
- **HUD** with active features and hotkeys (F5 god, F6 fly, F7 noclip, F8 teleport to crosshair, F9 kill target, F10 HUD, F11 ESP).

Settings and hotkeys: `BepInEx/config/com.Kavoshnik.firstmod.cfg`.
Saved builds: `BepInEx/config/FirstMod_builds/`.

Toggles are never saved: cheats do not turn on by themselves on launch.

## Installation
Install through Thunderstore Mod Manager, or put `FirstMod.dll` into `BepInEx/plugins/FirstMod/`.
