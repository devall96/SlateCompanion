## Slate Companion 0.5.2 (hotfix)

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.5.2/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.5.2/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.5.x or 0.4.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Hotfix: building from your game files no longer fails with "Access to the path …\gamemap.building is denied".** Nothing else changed.

### 🛠️ Fixed (Windows)
- **The first-start build could fail every time** with *Access to the path …\gamemap.building is denied*, even when running as administrator. At the end of the build, Slate Companion renames the folder with the new map, and Windows refuses that while another program has one of its files open, usually a virus scanner or the search indexer checking the thousands of new map files. Slate Companion now waits for them (up to about 20 seconds), and if the folder is still busy, copies the map into place instead.
- **A failed build no longer blocks the next one.** The half-built folder it left behind used to be deleted first at every new try, which could fail the same way. Now it's cleaned up when it can be, and doesn't stop a new build.
- Read-only files (some game dumps have them) no longer stop that cleanup.

If you saw this error: update, then press **Use these** on the welcome screen (or **Build from my game files** in Settings) once more.

> [!NOTE]
> Tested on Windows: the error was reproduced by holding a map file open during the build's last step, then fixed and checked with a file held for a few seconds, held throughout, read-only files, and full builds from game files over a failed build's leftovers and over an existing map. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
