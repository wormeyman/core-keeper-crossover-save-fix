# Other Core Keeper mods and game notes

Notes on other mods and game settings, gathered while playing (October 2026). This repo did not write any of these mods. The details come from each mod's own page, and we have not tested them under CrossOver unless a note says so.

## Installing mods

- Mods come from the **Steam Workshop** (subscribe on the mod's page) or from **mod.io**. The game loads them at startup, so restart it after adding or removing one.
- Many mods need **CoreLib**, a shared library mod. Install it too when a mod lists it.
- A mod that asks for **elevated access** has `skipSafetyChecks` turned on, like our CrossOverSaveFix mod. That lets it use file access and Harmony patches, which normal mods cannot. See [About "elevated access"](../README.md#about-elevated-access).
- **Under CrossOver:** the mod loader's temp-copy bug (see [The mod loader's temp copy](../README.md#the-mod-loaders-temp-copy)) can hit any mod, not just ours. If a mod loads once and is skipped on the next launch, close the game and empty `AppData/Local/Temp/Pugstorm/Core Keeper/ModLoader/` in the bottle.

## Building: blueprints and placement

The game has no blueprints or copy and paste for builds. Two mods help:

- **BuildingBlueprint** ([Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3753254765), also on mod.io) lets you select an area, save it, and paste it somewhere else. It needs CoreLib and elevated access. The mod.io copy is no longer updated; use the Workshop one. More below.
- **PlacementPlus** (mod.io) places walls, floor, tiles and seeds in lines and grids. It needs CoreLib.

### How BuildingBlueprint works

The Workshop download includes the mod's C# source, so these notes come from reading its code (`BuildingPlaceSystem.cs`, `BuildingSelectSystem.cs`, `SaveHandler.cs`) in November 2026. Most of it has not been tested in the game yet.

**Pasting uses your items.** Each tile and object you paste uses up one matching item from your inventory. If you don't have one, that piece is skipped, and the mod gives no warning. The blueprint panel shows how many of each item you need and how many you carry. Creative mode with God Mode on skips this check.

**Two layers.** Objects (machines, chests, torches, and so on) and tiles are selected separately. Switching layers keeps your selection, so one blueprint can hold both: box the objects, switch to the tile layer, box the tiles, then save.

- An object is only picked up when it fits fully inside the box. Its paint color is copied. What is inside a chest is not.
- The tile layer has a filter: dug-up ground (tilled soil), water, ground, floor, lit floor, loose flooring, rug, wall, thin wall, fence, rail, bridge, roof hole, slime ground, and Chrysalis. **Every type starts turned off**, so turn on the ones you want first.
- Click mode adds or removes one thing at a time. Use it to drop natural rocks (stalagmites) that a box picked up by accident.

**What each tile costs.** Tilled soil needs any hoe in your inventory, and the hoe is not used up. Water uses a filled bucket. Other tiles use the matching block or floor item.

**Crops can't be pasted** outside God Mode. A planted crop is its own game object (`CarrockPlant`), not the seed (`CarrockSeed`). The mod looks for the planted object in your inventory, which you can never carry. Copy the tilled soil, then plant the seeds yourself.

**Placing.** Pick a blueprint, choose one of 9 anchor points (corners, edges or center), and left-click. Right-click leaves paste mode. There is no rotate or mirror. The mod does not check whether the spot is free, so paste onto cleared, plain dirt. Tiles go down first (ground and water, then soil and bridges, then floors, then walls), then objects. You have to wait 1 to 3 seconds between pastes, depending on size.

**Destroy** removes everything you have selected. By default only an admin can use it (`DestoryPrivileges` in its `Config.cfg`).

**Keys.** **B** opens the panel. Quick Buff (below) also uses **B** by default, so change one of them under Controls.

**Where blueprints are saved.** Each blueprint is its own JSON file, in `mods/BuildingBlueprint/<folder>/<name>.json` inside the game's per-player data folder (on Linux, `~/.config/unity3d/Pugstorm/Core Keeper/Steam/<STEAM_ID>/`). The game keeps a `.pugbackup` copy of each. The panel's Copy button puts a blueprint's JSON on the clipboard, and Import reads one back, so you can share them. Edit the files only while the game is closed.

**Reading a blueprint outside the game.** The files store objects and tiles as numbers. `tools/blueprint_report.py` prints a map of a blueprint and the items one paste needs, with the names read from the game's `Pug.Base.dll`. It needs [uv](https://docs.astral.sh/uv/):

```fish
uv run tools/blueprint_report.py "<path to blueprint>.json"
```

Add `--managed "<path to CoreKeeper_Data/Managed>"` if the game is not in the default Linux Steam folder. It has only been tried on a blueprint with objects and no tiles.

## Automation parts used in farms

Gathered in November 2026 for blueprinting an automated crop farm. Each fact says where it came from: the game's code (read with the tools in this repo), the [Core Keeper wiki](https://corekeeper.atma.gg/en/Automation), or what a player saw in their own farm. Nothing here was tested on purpose in the game yet.

**Facing is stored as the variation number** (code: `DirectionBasedOnVariationCD.GetDirectionFromVariation` in `Pug.ECS.Components.dll`). 0 faces up (+y), 1 right, 2 down, 3 left. Some object types count from 1 instead, with 0 meaning "no direction". The code has a switch for that, and which objects use it is set in the game's data, not its code. BuildingBlueprint saves and pastes the variation, so pasted objects keep their facing. There is still no way to rotate a whole blueprint.

| Part | What it does | Source |
|---|---|---|
| Robot Farm Arm | Takes seeds from **behind** and plants them **in front**. Harvests ripe crops in front and puts the harvest behind, onto a belt, the ground or into a chest. Needs power. Does not make seeds. | wiki |
| Robot Arm | Moves items from behind to in front: off the ground, out of chests and furnaces, into chests and furnaces or onto the ground. Will not plant seeds. Can filter to one item type. | wiki |
| Sprinkler | Waters tilled soil 2 tiles in every direction (a 5 x 5 square). Needs no power or water. | wiki |
| Item Collector | Moves every item in a 5 x 5 square in front of it to the tile behind it. Needs power. | wiki |
| Conveyor Belt | Moves items 1 tile per second. **R** turns one belt tile in place. | wiki |
| Conveyor Belt Splitter | Splits a stack in half and sends the halves out its front and back. It has 4 facings, but facing only decides which side gets the bigger half first. | wiki |
| Seed Extractor | Turns crops into seeds. An automated farm needs one to keep replanting, because farm arms don't make seeds. The wiki says to send about half the harvest to it. | wiki |

**Arms are made of "movers"** (code: `MoverCD` in `Pug.Automation.Components.dll` has `start`, `stop`, `moveTime` and `cooldownTime`). One arm can have several movers and cycle between them, so one farm arm may work more than one tile. Which tiles is set by `affectedPositions` on `AutomatedHarvestAndMoverAuthoring` and `AutomatedMoveAndPlanterAuthoring`. Those values are baked into the game's compiled scene data, not its code, so **a farm arm's reach is still unknown.** Watch one arm in the game to find out.

**The wiki's "in front" rule is not the whole story.** In one working farm, two rows of farm arms have the same facing (variation 0). One row has its crops in front and its belt behind. The other has its crops behind and its belt in front. The player reports that both rows plant and harvest. So a farm arm seems to work both ways: it takes a seed from the belt on one side and plants it in the tilled tile on the other side, then moves the harvest back to the belt. If so, a farm arm's facing doesn't matter. That farm also has 22 farm arms and 22 crop tiles, which suggests each arm works one tile. Both points are inferred from one farm, not read from the game's code.

### A tileable crop farm design

[Simple and Efficient Automatic Crop Farming](https://youtu.be/ULqGPiiX-yM) by Nifegun (April 2026, 17 minutes) is a farm built to be copied: modules sit right next to each other without getting in each other's way. Notes from its transcript:

- **Layout.** A ring of farm arms around the crop tiles: a full row along the top and the bottom, plus one sideways arm at each end of the middle row. Two sprinklers sit inside. A conveyor belt loop runs around the **outside** of the arms and carries seeds past every arm. One crop type per module. The two-sprinkler version has 22 crop tiles. An eight-sprinkler version has 82.
- **Seed balance.** A chain of 6 belt splitters sends 33 of every 64 crops to a seed extractor, and the rest are kept. Each crop makes 2 seeds, and about 3% of crops come out golden. That gives about 1.0003 new seeds per crop harvested, just over what the farm needs, so seeds don't pile up.
- **Collection.** An item collector (the "vacuum") and robot arms with filters pull out the kept crops. One arm is filtered to the normal crop, one to the golden crop, and in the larger version one to the seed. Filters are not copied by BuildingBlueprint, so set them on every pasted copy.
- **Starting it.** Drop one seed per crop tile onto the loop (22 or 82). Put a bit more than that again into the seed extractor as crops, so you end up with a little over twice as many seeds as tiles. One set is in the ground, one is riding the loop, and some are in the extractor.
- **Power.** A dimly lit machine is still powered.
- **Building next to a running farm.** Fill your empty inventory slots with one stackable item, and carry one extra of each building piece. Otherwise a slot empties as you build, and you start picking up the crops and seeds that float on the belts. When done, sort your inventory to clear the filler. With BuildingBlueprint this matters too: a paste takes items out of your inventory, which can empty a slot.
- **Collection point.** Each module collects at its bottom-right corner, so a row of modules can share one belt that runs past all of them.

**Filters** (code: `ObjectFilteringCD`, `CategoryFilteringCD` and `MoverFilterCD`). A filter is a whitelist or blacklist of one item (with its variation) or of an item category. The wiki says you need the item in your inventory to set one. BuildingBlueprint does not copy filters, so set them again by hand after pasting.

One community guide ([cozy-checklist](https://cozy-checklist.pages.dev/core-keeper/guides/auto-golden-crop-farm), for game version 1.2) found that crop rows longer than 8 tiles leave empty plots, because the seed extractors fall behind. That is one player's observation, not a documented rule.

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

- [BuildingBlueprint - Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3753254765)
- [BuildingBlueprint - mod.io](https://mod.io/g/corekeeper/m/buildingblueprint)
- [PlacementPlus - mod.io](https://mod.io/g/corekeeper/m/placementplus)
- [SimpleTileGrid - mod.io](https://mod.io/g/corekeeper/m/simpletilegrid)
- [Player Coordinates HUD - mod.io](https://mod.io/g/corekeeper/m/player-coordinates-hud)
- [Core Keeper Mod SDK introduction - mod.io](https://mod.io/g/corekeeper/r/core-keeper-mod-sdk-introduction)
- [World - Core Keeper Wiki](https://corekeeper.atma.gg/en/World)
- [Steam discussion: chunk grid question](https://steamcommunity.com/app/1621690/discussions/0/4845400828046979493)
- [Core Keeper food buffs guide - BisectHosting](https://www.bisecthosting.com/es-es/blog/core-keeper-food-buffs-foods-combinations-effects-crit-attack-speed-damage)
- Steam Workshop details were read through Steam's public `ISteamRemoteStorage/GetPublishedFileDetails` API.
