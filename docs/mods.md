# Other Core Keeper mods and game notes

Notes on other mods and game settings, gathered while playing (October 2026). This repo did not write any of these mods. The details come from each mod's own page, and we have not tested them under CrossOver unless a note says so.

## Installing mods

- Mods come from the **Steam Workshop** (subscribe on the mod's page) or from **mod.io**. The game loads them at startup, so restart it after adding or removing one.
- Many mods need **CoreLib**, a shared library mod. Install it too when a mod lists it.
- A mod that asks for **elevated access** has `skipSafetyChecks` turned on, like our CrossOverSaveFix mod. That lets it use file access and Harmony patches, which normal mods cannot. See [About "elevated access"](../README.md#about-elevated-access).
- **Under CrossOver:** the mod loader's temp-copy bug (see [The mod loader's temp copy](../README.md#the-mod-loaders-temp-copy)) can hit any mod, not just ours. If a mod loads once and is skipped on the next launch, close the game and empty `AppData/Local/Temp/Pugstorm/Core Keeper/ModLoader/` in the bottle.

## Building: blueprints and placement

The game has no blueprints or copy and paste for builds. Two mods help:

- **BuildingBlueprint** (mod.io) lets you select an area, copy it and paste it somewhere else. It copies placed objects, several tile types (a filter picks which) and paint. It does not yet copy what sits inside containers. It needs CoreLib and elevated access. Your saved builds live in its generated config file, so don't edit that file. Its mod.io page says that copy can't be updated and points to a Steam Workshop copy, which we could not find. Check which copy is current before you install. We could not confirm whether pasting uses materials from your inventory or places things for free.
- **PlacementPlus** (mod.io) places walls, floor, tiles and seeds in lines and grids. It needs CoreLib.

## Seeing the grid

The game has no setting to show tile or chunk borders.

- **SimpleTileGrid** ([Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3751801931), also on mod.io) draws "a grid that shows the borders of tiles and spawn cells." Shift + F6 turns it on and off. You can change the key under Gameplay in the settings. Color and opacity are in a UI settings sub-menu. It needs no other mods. We use this one.
- **Player Coordinates HUD** (mod.io) shows your world position in the bottom-left corner, so you can find borders by their numbers.

### Grid sizes

People online argue whether a chunk is 16 x 16 or 64 x 64 tiles. Both grids exist. These values come from the game's own constants in `Pug.Base.dll`:

| Grid | Size in tiles | Constant |
|---|---|---|
| World chunk ("submap"), the area the game loads and saves | 64 x 64 | `ECS_SUBMAP_SIZE` |
| Mob respawn cell (4 x 4 of them per chunk) | 16 x 16 | `ECS_SUBMAP_RESPAWN_PART_SIZE` |
| Procedural spawn cell (4 x 4 chunks) | 256 x 256 | `PROCEDURAL_SPAWN_CELL_SIZE` |

The 16 x 16 cells matter for mob farms, since most mobs spawn once per cell. The 64 x 64 chunk matters for anything that should load as one piece, like a farm. According to the [wiki](https://corekeeper.atma.gg/en/World), the grid starts at the bottom-left corner of the default spawn tile. So any coordinate that is a multiple of 16 is a cell edge, and any multiple of 64 (0, 64, -64, 128 and so on) is a chunk edge.

**Laying out a 64 x 64 plot with SimpleTileGrid:** its "spawn cells" are most likely the 16 x 16 cells. The page does not say for sure. Every 4th line is then a chunk edge. To find which one, stand on a coordinate that is a multiple of 64 (Player Coordinates HUD shows it), and count 4 cells from there.

The game code also has a developer tool, `SubMapDebug`, with a `drawSubMaps` flag. We found no way to turn it on in a normal game.

## Eating food

The game has no setting to eat several foods at once. A cooking pot can combine two ingredients into one dish that has both of their buffs. If two buffs are the same type, only the stronger one is kept. These mods add one-key eating:

- **Quick Buff** ([Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3813043656)). **B** uses one of each different buff food and buff potion you carry. **F** does the same for cooked buff foods only, even when you aren't hungry. **H** heals. By default it skips an item when all of its buffs still have more than 30 seconds left, so food isn't wasted. Change that under Settings > Mod Options > Quick Buff, and the keys under Controls > Quick Buff. It looks in your backpack and pouches. It is also part of "Sid's Overhaul", so install one or the other, not both.
- **Quick Potion** ([Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3787267631)). **L** "uses all cooked foods in your inventory, from weakest to strongest benefit." The page does not say whether that means one of each food or whole stacks. **H** uses healing potions, **J** mana and **K** all buff potions. Remap keys under Settings > Controls > Mods. It needs CoreLib, and works in multiplayer and on dedicated servers.
- **AutoEat** ([Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3787215320)) eats cooked food by itself when hunger drops below a level you set (50% by default). Chat commands (Ctrl + X): `autoeat/hunger <1-100>`, `autoeat/on`, `autoeat/off`.
- **Inventory Quickuse - Backpack consume** ([Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3738908062)) lets you middle-click a food or potion in your backpack to use it, without moving it to the hotbar.

Quick Buff is the safer pick for buffs: it clearly uses one of each food and skips buffs that are still running. Don't install it together with Quick Potion, since both use **H**.

## Skipping the startup logos

There is no setting or launch option that skips the two company logos at startup. None of the game's roughly 60 command-line flags does it. `-benchmark` skips them, but it loads a benchmark scene instead of the game.

Press any key during each logo. Each one fades in for 1 second, holds for 4 seconds and fades out for half a second, 11 seconds in all. The hold (`LoadingScene.WaitWithInputInterrupt` in `Pug.Other.dll`) ends early on any button press, which cuts the wait to about 3 seconds. A small mod could skip the logos entirely by patching `LoadingScene.ShowLogo`, but we haven't built one.

## Making your own mod

This repo's mod shows how the game's mod system works: the game compiles a mod's C# files itself when it starts, so a mod is only a `ModManifest.json` plus `Scripts/*.cs`. See [How the mod works](../CLAUDE.md#how-the-mod-works-and-why) and the [Tools](../README.md#tools) section. `CompileCheck` compiles a mod against the game's own DLLs, and `InspectMethods` reads the game's code. Both run in Docker on a machine without .NET.

## Sources

- [BuildingBlueprint - mod.io](https://mod.io/g/corekeeper/m/buildingblueprint)
- [PlacementPlus - mod.io](https://mod.io/g/corekeeper/m/placementplus)
- [SimpleTileGrid - mod.io](https://mod.io/g/corekeeper/m/simpletilegrid)
- [Player Coordinates HUD - mod.io](https://mod.io/g/corekeeper/m/player-coordinates-hud)
- [Core Keeper Mod SDK introduction - mod.io](https://mod.io/g/corekeeper/r/core-keeper-mod-sdk-introduction)
- [World - Core Keeper Wiki](https://corekeeper.atma.gg/en/World)
- [Steam discussion: chunk grid question](https://steamcommunity.com/app/1621690/discussions/0/4845400828046979493)
- [Core Keeper food buffs guide - BisectHosting](https://www.bisecthosting.com/es-es/blog/core-keeper-food-buffs-foods-combinations-effects-crit-attack-speed-damage)
- Steam Workshop details were read through Steam's public `ISteamRemoteStorage/GetPublishedFileDetails` API.
