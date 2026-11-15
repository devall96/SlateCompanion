## Slate Companion 0.3.1

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.3.1/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.3.1/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.3.0? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Fixes: safer save changes, Equip buttons that click, Import and Restore on Windows, and a calmer app while you play.**

### 💾 Saves
- **Import all and Restore work on the save you're playing (Windows).** While Slate Companion followed a save, Windows wouldn't let that save folder be swapped, so both always said "The saves weren't changed". Following stops while saves change and starts again after.
- **A slot can't be left half-deleted.** Copying or importing into a slot deleted the old slot first; a file held open (Explorer, antivirus, OneDrive) could leave it half gone. The new slot now goes in beside it, the old one is moved aside and only deleted at the end. Deleting a slot moves it aside in one step first.
- **No more "The saves weren't changed" after they were.** Only a leftover folder couldn't be cleaned up; that's now a note in the log.
- **Changed files are written to disk before they're swapped in**, so a power cut can't leave an empty save. Exports read saves without blocking the game from writing them.
- **Nothing to change, no new backup.** Equipping what's already equipped no longer copies the whole save folder to a new backup.
- The Saves screen's slot list opens faster when a slot can't be read.

### 🎒 Inventory and 3D Link
- **The Equip and Take off buttons on pouch items work.** Dragging items took their clicks.
- **Equip checks it's the same item.** If the game saved meanwhile and another sword sits where yours was, Equip now refuses instead of equipping the other one.
- **3D Link isn't rebuilt on every click** (a category, a switch), which reloaded his shaders and textures each time.
- A damaged game file can no longer hang or crash the 3D view; the screens never get stuck with every button greyed out after an unexpected error.

### 🗺️ Map and the rest of the app
- **The map keeps your place.** Coming back from the game, it no longer jumps back to the shrine it was opened on, and no longer reloads every map picture after each alt-tab.
- Nothing ticks while Slate Companion is minimized or in the background (a timer and the first-run build's progress did), and a screen opened while minimized no longer keeps working after.
- Fixed a crash when drawing pictures after rebuilding from your game files, and a crash of the whole app if the build hit a damaged map file or a full disk (it now says the build failed). A failed rebuild keeps your working map.

### ▶️ Play screen (Savage Wind)
- **Installs, updates and Go back run in the background**: the window no longer freezes for seconds while files are copied or checked, and every error is shown (some used to vanish).
- **Closing Slate Companion, or updating it, in the middle of a Savage Wind install or update is held off** until it's done or cancelled, so it can't stop between two file swaps.
- **Final releases count as newer than their alphas** (0.6.0 after 0.6.0-alpha.3); before, alpha players would never have been offered the final release.
- A new Windows install puts `savagewind.exe` in last and takes everything out again if it fails, so it can't leave a copy that looks installed but has no version.
- 🐧 **An AppImage updated by Slate Companion shows its new version** straight away (it keeps its old file name), so its shader caches are reset for the new version as they should be. Its download no longer redraws the screen for every few kilobytes.
- 🐧 **Savage Wind's icon shows in the app menu**: taking it out of the AppImage could fail and then never be tried afresh.

### 🧰 Also
- Settings are saved more safely: changes from two places at once can't clash, and a `settings.json` that can't be read is kept aside (`settings.json.corrupt-…`) instead of being replaced by defaults.

> [!NOTE]
> Tested on Windows: every screen, the map's frame times (no change in the lows or hitches), the 3D Link picture (identical to 0.3.0), and slot copy, delete, restore and equip on test saves. The Linux build is checked to start by GitHub's test machine. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
