## Slate Companion 0.2.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.2.0/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.2.0/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already have 0.1.0? Open **Saves & settings → Updates → Check for updates**: from now on Slate Companion checks by itself when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Your Savage Wind launcher, your save files and your inventory.** Slate Companion can now install, update, set up and start Savage Wind with all of its settings, manage your saves (Cemu saves included), and show everything Link is carrying. The launcher is optional: you choose on the first start, and Settings can turn it off.

### ▶️ New: Play (the Savage Wind launcher, optional)
- **Play** goes straight into the game, past Savage Wind Configuration. Starting `savagewind.exe` yourself, or opening Savage Wind Configuration, works as always.
- **Install Savage Wind**: downloads the newest release from GitHub, checks it against Savage Wind's signature and every file against its list, and puts it in a folder you choose.
- **Your game files, found for you**: from another copy of Savage Wind (linked, not copied again, where the disk allows) or a dump on your PC; or choose the folder yourself.
- **Set up with progress bars, no Savage Wind screens**: the game files, the game code built from them and the shaders precompiled, with Savage Wind's own setup running hidden. A new copy was set up in about 7 minutes in testing.
- **Updates installed by Slate Companion**: the same steps as Savage Wind's own SW Update (signature, file list, the same undo journal and lock, a start check), then the new version is set up. **Go back to the previous version** until it's set up.
- **Several copies**: every copy on your PC, with your own names for them; pick which one Play starts, update, set up, go back, or remove one from the list (nothing is deleted).
- **All of Savage Wind's settings**: the same tabs, names, choices and descriptions as Savage Wind Configuration (controls, video, audio, language, saves, game files), plus launch options. Saved to that copy's `savagewind.ini` only while the game is closed.
- **Clear Skies**: a **Play with Clear Skies** switch and a picker for its 20 Clarity looks, **Mods & GameBanana**, and a notice when Clear Skies has an update.
- **Optional**: the first start asks whether you want it; **Settings → Savage Wind launcher** hides it. Hidden, Slate Companion never starts, changes or updates Savage Wind.

### 💾 New: Save files
- Every slot of every Savage Wind copy and save profile, with its picture, date and play time.
- **Back up** and **restore** (Savage Wind's own backup folder: its Saves tab can restore them too).
- **Import** a save from Savage Wind, a Switch, Ryujinx, Yuzu or a .zip, the whole save or one slot into another. **Cemu / Wii U saves are converted** on the way in, with a port of Savage Wind's own converter (byte for byte the same as Savage Wind's in testing).
- **Export** as a .zip, for Savage Wind or **converted for Cemu**.
- **Copy** a slot to another slot, profile or copy, and **delete** one.
- **Safe**: every change backs the whole save folder up first, takes Savage Wind's save lock, and never runs while the game is open. Tracking your progress still only ever reads.

### 🎒 New: Inventory
- Everything in Link's pouch, with the game's own pictures, names and descriptions: weapons, bows, arrows, shields, armor, materials, food and key items.
- Durability, counts, **weapon bonuses** (attack up, durability up, critical hit…), food effects, and armor ★ levels.
- **Rupees, Mon, hearts, stamina** and pouch sizes.
- **Paper doll** of what Link has equipped (weapon, shield, bow, arrows, head, body, legs) and your armor set's bonus. *The doll is a placeholder for now: a simple drawn outline. Link in his actual gear comes later.*

### 🗺️ Map
- The **Layers** list shows the map's own icons instead of coloured dots.
- Each **Divine Beast** has its own game icon on the map; memory spots (a camera) and Great Fairy fountains (a sparkle) get drawn icons, as the game has none for them.

### 🛡️ Armor
- The six jewelry pieces get a gem icon in their own colour (the game draws those icons from 3D models, so there's no picture to take) and their in-game description.

### 🔄 Slate Companion updates
- Checks for a new version **when it opens** (one small request to GitHub; installed copies only). A bar offers **Update and restart**, **What's new**, **Skip this version** and **Later**.
- **Saves & settings → Updates** turns the check off.

### 🧰 Errors and logs
- An **error window** when something goes wrong, with the details, **Copy details**, **Open logs folder** and **Report a problem**. A mistake in one screen no longer closes the app.
- A **log** (`logs/slatecompanion.log`, the previous run's kept) and **crash reports** (`logs/crash-*.txt`) in Slate Companion's folder, with your user name taken out of paths.

### 🌙 Lighter in the background
- **Clicking over to the game now puts it to sleep too**, not only minimizing: no timers, no polling, no drawing until you come back.
- **Settings → Live position** can keep the map following Link in the background, for a second screen (off by default).

### ✏️ Other changes
- The small gold "SHEIKAH SLATE" label above each screen's title is gone.
- Settings: a **Savage Wind launcher** section (show or hide it, minimize when the game starts), the update check switch, **Open logs folder** and **Report a problem**.
- Command line: `--sw-install`, `--sw-update` and `--sw-setup` run the Play screen's jobs with their progress printed; `--convert-save` converts a save; `--verify-sw` checks a Savage Wind download's signature.
- README: new screenshots, and credits for the parts of Savage Wind used (settings list, save converter, update steps; MIT) and Clear Skies.

> [!NOTE]
> **Early, made with AI, and not official.** Tested on one Windows PC with real Savage Wind saves and a real Savage Wind install. Not tried yet: updating an existing Savage Wind to a newer release (there hasn't been a newer release to update to), the Linux AppImage on a Linux desktop, converting saves between different game versions, and the weapon bonus and food effect names (none of the test saves have any). Written by Claude through Claude Code and directed and tested by devall96. Not affiliated with Nintendo or Savage Wind. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
