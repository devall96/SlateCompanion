# flags.json: sources, licenses and open questions

`flags.json` lists the save flags Slate Companion reads from BotW's `game_data.sav`. Every flag name in it comes
from one of the sources below. No flag name was guessed. Quest notes and steps are written fresh from the facts in the
game's quest-log text; no wiki or game prose was copied.

## Verification against real saves

Every flag in `flags.json` was checked by CRC32 hash against the provided read-only test saves:
18 `game_data.sav` and 18 `caption.sav` files from Switch 1.9.0 (revision 0x4730), under `testdata/mac/` (`playsave`,
`ubuntu-20261111` and `ubuntu-20261114`, slots 0-5). Each entry with a flag has `"inSave": true|false`. Every listed
flag was found. One flag, `AoC_HardMode_Enabled`, lives only in `caption.sav`. Its entry is marked
`"saveFile": "caption.sav"`.

The test saves also confirmed these points:
- **Play time.** No flag is named `PlayTime`. Play time is `PlayReport_PlayTime` (hash 0x73c29681, s32, seconds), the
  same hash Marc Robledo's editor reads. `PlayReport_AllPlayTime` is not stored in these saves.
- **caption.sav hashes.** The 14 hashes resolve to these flags: LastSaveTime_Lower, GameClear, IsChangedByDebug,
  SaveLocationName (string256), IsSaveByAuto, LastSaveTime_Upper, IsLogicalDelete, AlbumPictureIndex (s32[48]),
  IsGet_Obj_AmiiboItem, CaptionPictSize, SeakSensorPictureIndex (s32[3]), AoC_HardMode_Enabled, FamouseValue and
  SaveDistrictName (string256).
- **Defeated counters.** All 82 `Defeated_*_Num` counters are present as single s32 entries. For example,
  `Defeated_Enemy_Bokoblin_Junior_Num` reads 10.
- **Inventory.** `PorchItem` holds 6720 entries, which is 420 slots × 16 words. `PorchItem_Value1` and
  `PorchItem_EquipFlag` hold 420 entries each. Slot 0 is filled first and the list has no gaps; the first empty name
  ends it. Pouch order is weapons, bows, arrows, shields, armor, materials, food, then key items. Weapon values are
  durability × 100 (for example 2700 and 5000). Armor values are 0, or -1 for armor that cannot be dyed. The bonus
  arrays hold 20 weapon entries, 14 bow entries and 20 shield entries. `CookEffect0` and `CookEffect1` hold 60
  vector2 entries each (120 words).
- **Hearts and stamina.** `MaxHartValue` 20 means 5 hearts, so the value is in quarter hearts. `StaminaMax` 1000.0
  means 1 wheel.
- `KorokNutsNum` exists in the game data but is not saved (`IsSave: false`). The Korok count is `HiddenKorok_Number`.

## Sources

| Source | What was used | License |
|---|---|---|
| [marcrobledo/savegame-editors](https://github.com/marcrobledo/savegame-editors), `zelda-botw/` (commit b4db8cd111) | `zelda-botw.hashes.csv` (45,051 flag names with type codes); the editor's stat hashes (play time, rupees, hearts, stamina, Korok counter, unique-boss counters); weapon bonus bit values; dye colour list; heart and stamina value encoding | MIT (Copyright 2017-2023 Marc Robledo; includes Octicons). The editor credits MrCheeze's gamedata dump for its data. |
| [leoetlino/botw](https://github.com/leoetlino/botw) (commit f50b984f5e), a text dump of the game's ROM files | `GameData/Flag/*.yml` (every flag's name, type, IsSave, init and max values); `Quest/*.quest.yml` (quest internal names, type Main/Sub/Mini, steps and step flags, giver actor, location); `Message/Msg_USen.product.sarc` (English quest titles, quest-log text, NPC names, place names, enemy names, shrine names, memory titles); `Demo/*.demo.yml`; `Map/MainField/*/*.muunt.yml` (RememberTag placements naming each photo-memory demo) | No license file. The data is Nintendo game data; it was used only to read facts (names, flags, numbers). |
| [MrCheeze/botw-tools](https://github.com/MrCheeze/botw-tools) (commit 39e57e4731) | `event/Demo309_2.c` (memory flashback entries; champion memories call Demo150-153); `map_locations.js` (RememberTag coordinates); `ActorInfo.product.xml` (armor `armorStarNum` and `armorNextRankName`) | No license file. Decompiled game data. |
| [zeldamods/botw-event-viewer](https://github.com/zeldamods/botw-event-viewer) `d/RememberTag.json` (seen in GitHub code search) | Confirms the RememberTag event sets `IsPlayed_Demo126_0`...`IsPlayed_Demo138_0` after each photo memory | Not checked; only the flag names were taken. |
| [zeldamods.org wiki: Save Files](https://zeldamods.org/wiki/Save_Files) | Save layout: header, CRC32 IDs, strings and arrays as repeated entries, and the `pict_book/` folder of compendium JPGs named by actor | CC BY-SA 4.0. Facts only; no text copied. |
| [Pistonight/celer-presets](https://github.com/Pistonight/celer-presets) `botw/data/memories.yaml` (commit e96eb4046f) | Memory place names and coordinates, used to match RememberTag positions to memory titles | MIT |
| [emoritzx/botw-tracker](https://github.com/emoritzx/botw-tracker) `data/quests/MEMORY.dat` (commit 9c096e6282) | The in-game memory numbering 1-18 | MIT |
| [zeldaret/botw](https://github.com/zeldaret/botw) (decompilation) | Cross-check that `IsGet_MemoryPhoto_*` and `IsPlayed_Demo151_0` are real common flags | No license file. Only flag names were looked up. |

The scripts that produced `flags.json` sit in the session scratchpad (`flagscripts/`). They are not part of the app.

## Unconfirmed or partly inferred

- **The Master Sword memory (memory 18).** `flag: null`. A candidate is `IsPlayed_Demo109_2`, whose internal title
  calls it the Master Sword's flashback. No source ties it to the memory list.
- **Champion memories (2, 4, 6 and 10).** `IsPlayed_Demo150_0` to `IsPlayed_Demo153_0` are confirmed as the cutscenes
  called by the Goron, Rito, Zora and Gerudo memory entries. The test saves agree: Mipha's memory is set and the other
  three are not. No source states directly that the memory screen reads these flags.
- **Shrine for each shrine quest.** Only 13 of 42 quests name their shrine in the quest data, and those 13 are filled
  in. The other 29 have `"shrine": null`, because the quest-to-shrine link sits in map actor links that were not
  traced.
- **Side-quest rewards.** Rewards are filled only where the game's completion text states them (37 of 76). The rest
  are `null`.
- **Meaning of some stats.** These are inferred from the flag name, the game-data limits and the test-save values:
  `DungeonClearCounter` (shrines cleared), `FairyRevivalNum` (Great Fairies revived, max 4), `WM_BloodyMoonTimer`
  (unit unknown), and `DefeatedGolemNum`, `DefeatedForestGiantNum` and `DefeatedSandwormNum` (unique overworld
  bosses, per the editor).
- **Blood Moon count and distance travelled.** No such flags exist, so both are `flag: null`.
  `App_Pedometer_StepCount` exists but is 0 in every test save.
- **CookEffect0 and CookEffect1.** The array shape is confirmed. Field meaning is partly inferred: x looks like an
  effect id (-1 means none) and y an effect level; in CookEffect1, x looks like health restored.
- **`IsNewPictureBook_*` and `PictureBookSize_*`.** "Unviewed" and "-1 = no photo" are inferred from the names and
  init values.
- **Main quest order.** The listed order is a typical playthrough. The four Divine Beasts, Forbidden City Entry and
  The Hero's Sword can be done in any order.
- **Quest completion flag.** The completion flag is the `NextFlag` of the quest's last step, so most end in `_Finish`
  and some in `_Finished`. For example, `Water_Relic_Finished` and `HatenoMini_ThreeTree_Finished`.
- **Quest steps.** Each step was written from the quest-log text. No in-game playthrough was done to check them.
