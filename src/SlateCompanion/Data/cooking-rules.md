# BOTW cooking rules (for the Slate Companion simulator)

These are the rules `cooking.js` implements over `ingredients.json` and `recipes.json`. All text here is our own
summary; numbers come from the game's data files as dumped by the sources below.

## Sources and how much to trust each

| Tag | Source | What it gives | License |
|---|---|---|---|
| **[DECOMP]** | `zeldaret/botw`, `src/Game/Cooking/cookManager.cpp` (decompilation of the game's `uking::CookingMgr`) | The control flow and arithmetic of cooking. Functions named below. | No license file (we only read it; no code copied) |
| **[PIS-DATA]** | `Pistonite/botw-recipe`, `research/output/actor-data.yaml`, `cookdata-recipes.yaml`, `cookdata-system.yaml` | Every ingredient's parameters; the recipe table in game order; the System constants | MIT |
| **[PIS-SIM]** | `Pistonite/botw-recipe`, `packages/cook/src/cook_mgr.rs` (based on savage13's simulator) | A simulator its author validated against a full dump from real hardware (1,805,568,402 inputs, HP, price, crit) | MIT, cook package BSD-2-Clause |
| **[SAV-TEST]** | `savage13/cooking.rs`, `t/*.json` | 617 in-game results, each recorded from a screenshot | BSD-2-Clause |

`Oasiris/botw-cookbook` was looked at but has **no license**, and nothing from it is used.

Verification: the Python twin `cooksim.py` reproduces **616 of the 617** [SAV-TEST] results (name, hearts, price,
plus effect, level and duration where recorded). The one miss is a Monster Extract dish, whose duration is random
(see below). `cooking.js` and `cooksim.py` gave identical output on 3,422 inputs when run in a browser.

## Units

* Hearts are **quarter hearts** (4 = one heart), as the game stores them (`cureItemHitPointRecover`).
* `potency` = `cureItemEffectLevel`. `timeBoost` = `cookSpiceBoostEffectiveTime` (seconds).
  `hpBoost` = `cookSpiceBoostHitPointRecover`. `critBoost` = `cookSpiceBoostSuccessRate` (%).
* `cureItemEffectiveTime` (900 on many items) is **not used** by cooking. [DECOMP]

## 0. Input

* 1 to 5 items; a 6th is ignored. Duplicates are allowed. [DECOMP `cookWithItems`]
* The game first merges duplicates into **distinct ingredients with a count**, in first-seen order
  [DECOMP `prepareCookArg`]. "Distinct" matters for recipe matching, spice boosts and crit chance;
  "count" matters for hearts, potency, duration and price.

## 1. Choosing the dish (recipe table)

The table (`recipes.json` → `table`) has two lists, read top to bottom; **the first match wins**.
[DECOMP `cook`, data from PIS-DATA]

* **Recipes** (125 entries). Each entry has a list of *actor groups* and a list of *tag groups*.
  * Each group is a slot. A slot is filled by one **distinct** ingredient that is in the group (actor id), or has one
    of the group's tags. The group's members are tried in order; for each, the first unused distinct ingredient that
    fits is taken. All copies of that ingredient go with it.
  * Actor groups are filled before tag groups. Every group must be filled. **Leftover ingredients are allowed.**
  * An entry is skipped when it has more groups than there are distinct ingredients.
* **SingleRecipes** (13 entries). Used only when there is exactly **one distinct non-spice ingredient**
  (anything without the `CookSpice` tag; any number of copies, optionally with spices) and the Recipes list found
  nothing, or when only one distinct ingredient was used at all. The ingredient must be in the entry's actor list, or
  have one of its tags.
* No match → **Dubious Food**.

```
uniq = distinct(items)
if len(uniq) > 1:
    for e in table.recipes:                       # in order
        if len(e.actors) + len(e.tags) > len(uniq): continue
        used = [False]*len(uniq)
        ok = all(fill(group, by_actor) for group in e.actors) and all(fill(group, by_tag) for group in e.tags)
        if ok: return e
    nonSpice = [u for u in uniq if 'CookSpice' not in u.tags]
    single = nonSpice[0] if len(nonSpice) == 1 else None
else:
    single = uniq[0]
if single: first e in table.singleRecipes where single in e.actors or single has a tag in e.tags
else / none: Dubious Food
```

Consequences of the table order (first six entries): a **Fairy** with an ore, critter or monster part gives
Fairy Tonic; otherwise **any ore, gem or Wood** (`CookOre`) gives **Rock-Hard Food**; a **critter + monster part**
gives an **Elixir**; any other mix containing a critter or monster part gives **Dubious Food**. Food ingredients added to
an elixir do **not** spoil it (e.g. Cold Darner + Ancient Core + Frozen Bird Thigh = Chilly Elixir) [SAV-TEST].
Dragon parts, Star Fragments and Monster Extract are spices, so they never block a recipe. Roasted and frozen
ingredients have **no category tags**, so they only fill slots that name them directly.

Check: for every 1- and 2-distinct-ingredient input, this flow gives the same dish as the simpler flow in
[PIS-SIM]/savage13 ("single list only when one distinct item").

## 2. Effect type

[DECOMP `cookCalcIngredientsBoost`] For every distinct ingredient that is **not** a monster part (`CookEnemy`) and has
`potency > 0`: add `count` to that effect's item count and `potency × count` to its potency.

* If two or more different effects appear → **no effect** (they cancel), duration 0.
* Monster parts never contribute an effect, only time. Critters do contribute (they are not `CookEnemy`).
* Goron Spice stores level 1 with effect type None; it adds nothing.

## 3. Effect level from potency

`level = floor(potency × rate)` in 32-bit float, then raised to 1 if between 0 and 1, then capped at `max`
[DECOMP `cookCalcIngredientsBoost`, `cookAdjustItem`; rates from PIS-DATA `cookdata-system.yaml`].
This equals the threshold tables in [PIS-SIM] for every potency up to 80 (checked).

| Effect (game name) | rate | max | Level 2 at | Level 3 at | Base time per effect item |
|---|---|---|---|---|---|
| chilly (ResistHot) | 0.35 | 2 | 6 | – | 120 s |
| spicy (ResistCold) | 0.35 | 2 | 6 | – | 120 s |
| electro (ResistElectric) | 0.5 | 3 | 4 | 6 | 120 s |
| fireproof | 0.3 | 2 | 7 | – | 120 s |
| hasty (MovingSpeed / AllSpeed) | 0.45 | 3 | 5 | 7 | 30 s |
| mighty (AttackUp) | 0.45 | 3 | 5 | 7 | 20 s |
| tough (DefenseUp) | 0.45 | 3 | 5 | 7 | 20 s |
| sneaky (Quietness) | 0.35 | 3 | 6 | 9 | 90 s |
| energizing (GutsRecover / "StaminaRecover") | 1.4 | 15 | level = fifths of a stamina wheel; restores `level/5` wheels (game stores level × 200) | | – |
| enduring (ExGutsMaxUp / "GutsPerformance") | 0.5 | 20 | level = fifths of an extra wheel; adds `level/5` wheels (5 Endura Carrots → 10 = 2 wheels) | | – |
| hearty (LifeMaxUp) | 1.0 | 108 | level = extra **quarter** hearts (yellow) | | – |

Energizing by potency: 1→1, 2→2, 3→4, 4→5, 5→7, 6→8, 7→9, 8→11, 9→12, 10→14, 11+→15 (fifths of a wheel).

## 4. Duration (timed effects only)

[DECOMP `cookCalcIngredientsBoost`, `cookCalcSpiceBoost`, `cookCalcRecipeBoost`, `cookAdjustItem`]

```
time = 30 × (total item count, all copies, including monster parts and spices)
     + baseTime(effect) × (count of items carrying the effect)
     + Σ monster part timeBoost × count
     + Σ timeBoost of each DISTINCT CookSpice item that is not a monster part (once each)
     (+ recipe TB, see uncertain list)
clamp to 0..1800 s
```

Examples [SAV-TEST]: Cold Darner + Lizalfos Tail = 60 + 120 + 160 = 340 s (5:40). 2 Voltfruit = 60 + 240 = 300 s.
Non-timed effects (hearty, energizing, enduring) and no-effect dishes have no duration.

## 5. Hearts

[DECOMP `cookCalcIngredientsBoost`, `cookCalcSpiceBoost`, `cookCalcRecipeBoost`, `cookAdjustItem`]

```
raw  = Σ hearts × count  over non-monster-part items
hp   = raw × 2                                  # lifeRecoverMultiplier
hp  += Σ hpBoost of each DISTINCT CookSpice item that is not a monster part   # nuts +2, dragon parts +5..15
hp  += recipe heartBonus (HB from the table, may be negative: Fairy Tonic -12, Honey Candy -8 ...)
hp   = clamp(trunc(hp), 0, 120)                  # 120 = 30 hearts
if no effect and hp == 0: hp = 1
```

**Hearty** dishes: the hearts value is replaced by the hearty level: `level = potency` (plus any monster
`MaxHeartLevel` boost, which no item has), rounded **up** to a whole heart, at least 4, at most 108.
In game: full heal plus `level/4` extra yellow hearts. `cook()` returns `hearts = level`, `fullRestore = true`,
`extraHearts = level/4`. (All hearty ingredients have potency in whole hearts, so the rounding never triggers.)

## 6. Critical cook ("critical success")

[DECOMP `cookCalcCritBoost`, `cookHandleCrit`]

* Chance = highest `critBoost` among the distinct ingredients + `[5, 10, 15, 20, 25]%` for 1..5 distinct ingredients.
  ≥ 100 → guaranteed (Star Fragment and every dragon part give 100; Cane Sugar, guts and similar give 30).
* The roll happens **after** the base hearts/effect and **before** spice and recipe bonuses.
* What a crit adds:
  * no effect: +12 quarter hearts (3 hearts);
  * hearty: +4 (one extra yellow heart);
  * energizing / enduring: +2 fifths of a wheel, or +3 hearts if the effect is already maxed; if hearts are full, the
    effect; otherwise 50/50;
  * timed effects: random among +3 hearts, +1 level, +300 s (5:00), skipping options that are already maxed (time is
    always possible).
* Dubious and Rock-Hard food never crit (they fail before the roll).
* `cook()` only reports `critical: "possible" | "guaranteed" | "no"` and `critChance`; it never applies the bonus.

**Blood Moon:** the community reports that cooking while the Blood Moon is rising guarantees a crit. In the decomp
this is the `always_boost` flag passed in by the caller, which forces the crit path; we did not trace which caller sets
it. `cook(ids, data, {bloodMoon: true})` returns `critical: "guaranteed"`. **Uncertain** (who sets the flag and the
exact time window).

## 7. Elixirs, Fairy Tonic, Dubious and Rock-Hard food

* **Elixir** (`Item_Cook_C_17`): needs a critter (`CookInsect`) and a monster part (`CookEnemy`) and wins over
  everything below it in the table. If the result has **no single effect** (no effect, or two that cancel) it becomes
  Dubious Food [DECOMP: `isMedicine` check]. Its name comes from the effect: Chilly, Spicy, Electro, Fireproof,
  Hasty, Sneaky, Mighty, Tough, Enduring, Energizing, Hearty Elixir. Critters give the effect and potency; monster
  parts give time (horn 40 s, fang 80 s, guts/tail 160 s, ...) and crit chance.
* **Fairy Tonic**: any effect is removed; heal = raw × 2 − 12; always sells for 2 rupees. One Fairy = 28 (7 hearts).
* **Dubious Food** [DECOMP `cookFail`]: heal = max(4, raw sum of hearts **not doubled**, no bonuses); no effect;
  sells for 2.
* **Rock-Hard Food**: heals 1 quarter heart; sells for 2.

## 8. Sell price

[DECOMP `cookCalcItemPrice`]

```
if Fairy Tonic: 2
sell = Σ sellPrice × count   (items tagged CookLowPrice — Star Fragment, dragon parts — count as 1 rupee each)
buy  = Σ buyPrice  × count   (CookLowPrice: 1 each)
sell = trunc(float32(mult[total count]) × sell)   mult = 1.5, 1.8, 2.1, 2.4, 2.8 for 1..5 items
if sell % 10: round UP to the next multiple of 10
price = max(min(sell, buy), 2)
```

The multipliers are 32-bit floats in the game (1.79999995 ...); use `Math.fround` / float32 to get exact results.

## 9. Monster Extract

[DECOMP `cookHandleMonsterExtract`] If Monster Extract is in the pot (and the dish is not Dubious/Rock-Hard), the
normal crit is replaced by a random roll: hearts +3 hearts, or hearts set to a quarter heart; or (if there is an effect)
effect +1 level, or effect set to level 1. Any timed effect's duration becomes 1:00, 10:00 or 30:00 at random. A
hearty dish or a dish with no healing skips the heart outcomes. `cook()` returns the unrandomised values plus a
warning.

## 10. Uncertain or unverified

1. **Monster Extract durations**: [SAV-TEST] records 1:20 for Monster Extract + Armored Porgy + Raw Meat, while the
   decomp says the time becomes 60/600/1800 s. Our 110 s (unrandomised) does not match either. Treat extract output as
   indicative only.
2. **Recipe time bonus (`TB`)**: the decomp reads an optional `TB` key per recipe; the decoded table we used only
   kept `HB`. All 104 recorded durations match without any TB, so it is probably absent or zero.
3. **Blood Moon** guarantee: see section 6.
4. **Dish name prefix**: we show "Hasty Mushroom Skewer" style names (effect adjective + dish) for food with an effect.
   This is how the game displays them as far as we know, but it was not checked against the game's text files.
5. **Hearty rounding**: decomp rounds up to a whole heart, Pistonite's simulator rounds down. No real input reaches the
   difference.
6. Critical-hit bonus choice for energizing/enduring is from code marked "NON_MATCHING" in the decomp (logic is
   believed right but not byte-identical).
7. Prices of some roasted items look odd (e.g. Roasted Bird Thigh: sell 24, buy 28, so its dishes are capped low).
   They come straight from the game data and are what the price cap uses.
