# Content sources: reference data and guides

This file covers `compendium.json`, `armor.json` and `guides/` (`walkthrough.json`, `shrines.json`, `tips.json`).
Other data files list their own sources (for example `FLAGS-SOURCES.md`).

Slate Companion is a fan-made app. The Legend of Zelda: Breath of the Wild and every name in it belong to Nintendo.
No images are bundled. `compendium.json` holds only remote image URLs, which the app loads online.

## compendium.json

- **Source:** Hyrule Compendium API v3, `https://api.hyrule-compendium.com/api/v3/compendium/all?game=botw` (fetched 2026-11-14). It returns 389 entries, the same set as the default with no `game` parameter. Source code: https://github.com/gadhagod/Hyrule-Compendium-API
- **License:** MIT, Copyright 2020 Aarav Borthakur. Keep this notice with the data:

  > Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

- **What changed:** the API's `description` text was dropped, so none of its prose ships. Kept: `id`, `name`, `category`, `common_locations`, `drops`, `cooking_effect`, `hearts_recovered`, `edible`, `properties` (attack/defense) and the remote `image` URL. Names are left lowercase, as the API gives them.
- **Note:** the API's `dlc` flag is false on every entry, so the file does not mark DLC entries.

## armor.json

- **Facts** (piece list, slot, base defense, effect name, upgrade materials, where found, price) come from these sources:
  - Zelda Wiki on Fandom (`zelda.fandom.com`): the "Armor/Breath of the Wild" list page and each armor page. Fetched through the MediaWiki API; only facts were extracted. The wiki's text is CC BY-SA, and none of it was copied.
  - TheJaredWilcurt/botw-armor-upgrade-tracker (MIT, Copyright 2022 The Jared Wilcurt): upgrade materials per level and set membership.
  - BinarMorker/ZeldaGear `items.json` (no license stated): used only to cross-check materials, never copied as the sole source.
- **Materials:** each upgrade level uses the materials and counts that at least two of the three sources agree on. Where no two agree, `materials` is `null`. That happens once: Flamebreaker Helm level 2.
- **Defense:** per-level values follow the standard ladder for each base defense (2→4/6/9/16, 3→5/8/12/20, 4→7/12/18/28, 5→8/14/22/32). They are `null` for the Hero set (8-bit Link amiibo) and the Topaz Earrings, where the wiki gives a maximum that contradicts the ladder.
- **Rupees:** Great Fairy upgrades cost materials only, so `rupees` is 0. The fairy unlock cost depends on how many fountains you have already opened: 100, 500, 1,000 and 10,000 rupees.
- **Set bonuses and locations:** taken from the wiki's Set Bonus and set pages, and from shrine treasure lists. `whereFound` notes are short and written in our own words.

## guides/shrines.json

- **Facts:** the shrine list (120 base and 16 from The Champions' Ballad), trial names, region, sub-area, location and treasure come from Zelda Wiki on Fandom ("Ancient Shrine" page and each shrine page).
- **Text:** `trial`, `solution`, `chests` and `quest` were written fresh from those pages. No sentences were copied. Where a page was a stub or unclear, the solution or chest count is left out.

## guides/walkthrough.json and guides/tips.json

- **Text:** written from scratch in our own words.
- **Facts checked against Zelda Wiki on Fandom pages:**
  - Quest pages: the four Divine Beast quests, Locked Mementos, The Hero's Sword, EX The Champions' Ballad and EX Champion Mipha's Song.
  - Trial of the Sword, Master Mode, One-Hit Obliterator, Master Sword and Blood Moon.
  - Hestu, Kilton, Kochi Dye Shop, Stable and Malanya.
- **Hestu numbers:** the Korok seed costs (441 to max all pouches) are taken from the wiki's Hestu table.

## Scripts

The fetch and build scripts were one-off helpers and are not part of the app. Downloaded code was never run.
