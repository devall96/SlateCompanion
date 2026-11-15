## Slate Companion 0.1.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.1.0/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.1.0/SlateCompanion.AppImage) (make it executable, then run it)
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**The first release: a Sheikah Slate for your PC.** Slate Companion reads your Breath of the Wild save while you play and ticks everything off by itself, shows the in-game map with the game's own icons, and keeps a walkthrough, every quest, a cooking simulator, the Hyrule Compendium, armor upgrades and your stats in one place.

> [!CAUTION]
> **Bring your own game files.** No game content ships with Slate Companion. On first start it asks for your Savage Wind folder (your own, legally obtained copy of the game) and builds the map, icons, pictures and in-game text from it, once, on your PC.

### 🗺️ The map
- **The in-game map**, built from your game files at 3 pixels per game unit, so it stays sharp at full zoom.
- **The game's own icons** for shrines, towers, Korok seeds, stables, villages, the castle and Tech Labs. Done things turn blue, or hide.
- **Your save spot, your own pins** (right-click), and **the photo from your album** at each captured-memory spot.
- **Link's live position** from Savage Wind is ready in the app; it needs a Slate Companion switch that isn't in a Savage Wind release yet.

### ✅ Your progress, from your save
- **All 136 shrines, 900 Korok seeds, 15 towers, the Divine Beasts, memories and 133 quests** tick themselves off as you play.
- Finds **Savage Wind** (every save profile), Cemu, Ryujinx and Yuzu-family saves by itself, or drop a save on the window.
- **Only ever reads your save.** It's opened read-only after the game has finished writing it, and never changed.

### 📖 Everything else
- **Quests & walkthrough** with the **game's own quest log**, **shrine solutions**, **Korok seeds** by map square or nearest first.
- **Cooking**: a pot simulator using the game's own rules (checked against 617 real results), with **item icons** and the **game's descriptions**.
- **Hyrule Compendium** with **its photos** and entries, **armor** with icons and a Great Fairy **shopping list**, **stats** with play sessions, and **guides**.
- **Ctrl+K** searches everything.

### 🌙 Light enough to leave open
A native app drawn in software, at below-normal priority, that does nothing until something changes and **goes to sleep when minimized**, so it stays out of your game's way.

> [!NOTE]
> **Early, made with AI, and not official.** Tested on one Windows PC with real Savage Wind saves; the Linux AppImage hasn't been tried on a Linux desktop yet. Written by Claude through Claude Code and directed and tested by devall96. Not affiliated with Nintendo or Savage Wind. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
