## Slate Companion 0.3.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.3.0/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.3.0/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.2? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Link in 3D in his own gear, loadouts, the game's cooking pot, and a map that keeps up.**

### 🧝 New: Link in 3D (Inventory)
- **Link in what he's wearing**, drawn live on your graphics card from your own game files: his body, every armor piece (upgraded ones too), and the sword, shield and bow on his back. A helmet hides his hair; with nothing on, he wears his default hair and shorts. This replaces the placeholder outline from 0.2.
- **Drag to turn him**, double-click to face him front again.
- **Your armor set's bonus** shows underneath when you wear a full set.
- It draws only when something changes (a drag, new gear), and nothing at all while the window is minimized or in the background. If your PC can't use OpenGL, or you draw in software, you get a still picture of him instead.

### 🎒 New: change your equipment and loadouts
- **Change equipment** (a switch on the Inventory screen, off until you turn it on): **Equip** and **Take off** on any weapon, bow, arrows, shield or armor, or **drag an item onto Link's slot**. The game's own rules: one item per slot, arrows only with a bow.
- **Loadouts**: save what you're wearing, or build a set from what's in your pouch (pick it or drag it into a slot; "leave as it is" keeps a slot), **preview it on 3D Link**, and **put it on with one click**. Items no longer in your pouch are marked missing and skipped. Loadouts are kept in Slate Companion, not in your save.
- **Safe**: this edits your save, so it only works while Savage Wind is closed, takes Savage Wind's save lock, backs the whole save folder up first (Savage Wind's own backups) and checks the written file before swapping it in.

### 🍳 New: the cooking pot
- **The game's own cooking pot**, stand and fire, drawn once from your game files. **Drag ingredients into it**, or click them; click one in the pot to take it out.

### 🗺️ Map: much faster
- Slate Companion now **draws with your graphics card** (Direct3D on Windows, OpenGL on Linux; software if the graphics card can't be used, or by choice in Settings). Still nothing is drawn while it's minimized or in the background.
- Measured on Windows, maximized, panning and zooming: **19.8 → about 118 fps** on average, the 1% lows **16 → about 70 fps**, the median frame **50 → 7 ms**, and frames over 33 ms **1,427 of 1,440 → none**.
- It also no longer makes new brushes and shapes on every frame.

### ▶️ Play screen
- **Options your PC can't use are greyed out**, with why: DLSS without an NVIDIA RTX card, FSR 4 without AMD's FSR 4. It reads what Savage Wind itself found on your PC the last time it ran, or your graphics card's name.

### 🐧 Linux
- **Add to applications**: Slate Companion and each Savage Wind AppImage it knows show up in your app menu (with Savage Wind's own icon, and **Play** and **Savage Wind Configuration** actions), ready to pin. A switch on the first start and in Settings; turning it off removes them again.
- **Pick your Savage Wind AppImage**: on the first start (and in Add a save), choose the AppImage you play with; its saves and game files are found from it.

### 🏠 Adventure log
- **Your stats are on the Adventure log** now (rupees, hearts, stamina, spirit orbs, Mon, days, Great Fairies, bosses, progress over time and play sessions); the separate Stats tab is gone.
- The enemies-defeated counts are gone: the game's own counters weren't right.

### ✏️ Other changes
- Weapon bonuses and food effects read correctly, checked against 98 real saves: "Attack up +15 (yellow)", "5-shot burst", "Hearty: +5 extra hearts", "Energizing: restores 1 wheel".
- Settings: **Draw with the graphics card**, **Add to applications** (Linux).
- Command line: `--app-menu on|off` (Linux), `--equip`, `--render-doll`, `--render-actor`; `SLATE_MAP_BENCH=<file>` runs the map benchmark (frame times, lows, hitches).
- New screenshots.

> [!NOTE]
> **Early, made with AI, and not official.** Tested on Windows with real saves: the 3D Link, the pot, equipping on a test save (done, refused when it should be, and undone to the exact same file), and the map benchmark. Not tried yet on Linux: the 3D view, Add to applications (checked by GitHub's build) and the AppImage picker. Written by Claude through Claude Code and directed and tested by devall96. Not affiliated with Nintendo or Savage Wind. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
