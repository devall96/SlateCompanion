# Map data sources

Collected 2026-11-14. Only facts were kept: names, coordinates, flag names and hashes. No images or prose were copied.
Coordinates are game world units: x (east-west), y (height), z (north-south), the same ones objmap uses.

| Source | License | What was taken |
|---|---|---|
| https://github.com/zeldamods/objmap (`src/MapBase.ts`, `src/util/map.ts`, `src/services/MapMgr.ts`, `korok_ids.json`) | GPL-3.0 | Tile URL pattern, zoom levels, CRS transformation (re-implemented as numbers in `tiles.json`); korok ids (e.g. `A01`) and full-precision korok coordinates from `korok_ids.json` |
| https://objmap.zeldamods.org/game_files/maptex/{z}/{x}/{y}.png | Nintendo game art served by zeldamods | Nothing stored. Loaded at runtime only (`tiles.json`) |
| https://objmap.zeldamods.org/game_files/map_summary/MainField/static.json | zeldamods data extracted from the game | Shrine, tower, village, stable, castle, landmark and lab positions, MessageIDs and `Location_*` discovery flags |
| https://objmap.zeldamods.org/game_files/text/StaticMsg/Dungeon.json and `LocationMarker.json` | zeldamods data extracted from the game | English names of shrines, towers, places and Divine Beasts |
| https://objmap.zeldamods.org/game_files/names.json | zeldamods data extracted from the game | Actor-to-name lookups for Hestu (`Npc_OldKorok*`) and the Great Fairy fountains (`TwnObj_FairySpring_*`) |
| https://radar.zeldamods.org (objmap backend; `/objs/{mapType}/{mapName}?q=...`, `/obj/{objid}`, `/obj/{mapType}/{mapName}/{hashId}`) | zeldamods service; no license stated | Korok actor names (Ground/Fly), hash ids and puzzle kind (`korok_type`); tower terminal link chains (confirming `MapTower_NN`); Divine Beast, Hestu, Great Fairy fountain and memory spot (`RememberTag`) positions |
| https://github.com/MrCheeze/botw-tools (`event/RememberTag.c`, `event/Demo309_2.c`, `event/BalladOfHeroZora.c`, `event/Demo004_0.c`) | No license stated | Flag names only: `IsPlayed_DemoNNN_0` set when a memory is recovered (and its PhotoNo); `Clear_DungeonNNN` counted as cleared shrines in the Champions' Ballad flow; `MapTower_07` set by the tower activation demo |
| https://github.com/zeldaret/botw (`gdtCommonFlags.cpp`, `uiPauseMenuDataMgr.cpp`) | No license stated (decompilation) | Only checked: `Clear_Remains{Wind,Fire,Water,Electric}` are bool game-data flags the pause menu reads for Divine Beast completion |
| https://github.com/marcrobledo/savegame-editors (`zelda-botw/zelda-botw.hashes.csv`, `zelda-botw.data.js`) | MIT | Cross-check only: every flag name written to `markers.json` and `koroks.json` appears in its save flag list, and all 900 korok hashes match its `KOROKS` list |
| https://github.com/lud99/botw-unexplored (`source/Data.cpp`) | No license file | Cross-check only: its 900 korok flag names (`MainField_Npc_HiddenKorok{Ground,Fly}_<hashId>`) match ours exactly, and its 120 shrine hashes are crc32 of `Location_DungeonNNN` |

## Flag meaning

- `flag` / `flagHash`: the save flag that becomes true when the item is done, plus `zlib.crc32(flag)` as an unsigned int (the key stored in `game_data.sav`).
  - Shrines: `Clear_DungeonNNN` (completed). DLC shrines are `Dungeon120`-`Dungeon135`.
  - Towers: `MapTower_NN` (activated). Tower terminal `TowerTerminalNN` -> event `Demo004_0` -> `LinkTag` with `SaveFlag MapTower_NN`, traced for all 15 towers.
  - Divine Beasts: `Clear_RemainsWind` (Vah Medoh), `Clear_RemainsFire` (Vah Rudania), `Clear_RemainsWater` (Vah Ruta), `Clear_RemainsElectric` (Vah Naboris).
  - Memories: `IsPlayed_DemoNNN_0` (the memory at that spot has been recovered).
  - Koroks: `MainField_Npc_HiddenKorok{Ground|Fly}_<hashId>` (seed collected).
- `discoveredFlag`: the `Location_*` s32 flag from the game's map data. It is non-zero once the place is discovered. This is not completion.
- Stables, villages, landmarks, labs, Hyrule Castle, Great Fairy fountains and Hestu have no completion flag in this data.
