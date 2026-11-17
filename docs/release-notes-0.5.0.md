## Slate Companion 0.5.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.5.0/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.5.0/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.4.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**The Savage Wind launcher is gone: it was redundant and buggy.**

### ▶️ Removed: the Play screen (Savage Wind launcher)
- **The Play screen is removed**, with everything that came with it: installing, updating, setting up and going back between versions of Savage Wind, starting it straight into the game, Clear Skies, and the copy of Savage Wind Configuration's settings.
- **Why:** Savage Wind already does all of this itself (its own updater, setup and Configuration screen), so Slate Companion was doing the same job a second time, and its version kept running into bugs. Start Savage Wind the way you normally would; Slate Companion keeps reading your save while you play, as before.
- **Nothing of yours is touched.** Your copies of Savage Wind, their settings and your saves stay exactly as they are. Slate Companion still finds your Savage Wind folders for your saves and game files, and still won't change a save while the game is running.
- The first-start question and the Settings switch for the launcher are gone too. 🐧 On Linux, the app-menu entries Slate Companion made for your Savage Wind AppImages are removed the next time it updates its own entry (Savage Wind's own entries aren't touched).

> [!NOTE]
> Tested on Windows: every screen opens, saves are found and read from Savage Wind folders, and the screenshots on the download page were taken again with this version. The Linux build is checked by GitHub's test machine, including that the old app-menu entries are cleaned up. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
