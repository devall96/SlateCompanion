## Slate Companion 0.2.1 (hotfix)

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.2.1/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.2.1/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.2.0? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**A fix for Linux: Play kept asking to "Set up and play" a Savage Wind that was already set up.**

### 🐧 Fixed (Linux)
- **"Set up and play" came back every time.** On Linux, Slate Companion took Savage Wind's version from its AppImage's file name. But an AppImage keeps its file name when Savage Wind updates itself (a `Savage_Wind-0.5.1-alpha.5-x86_64.AppImage` can hold 0.5.3-alpha.5), so the version never matched the one Savage Wind had set up, and Play asked to set it up again, even right after it had. It now reads the version Savage Wind itself writes at every start (its `savagewind.log`), and only falls back to the file name when that log is older than the AppImage.
- Because of the same mix-up, the first "Set up and play" also emptied the shader caches for a version that wasn't new. That doesn't happen any more.

Windows wasn't affected: there the version comes from `sw-manifest.txt`, which every update replaces.

### 🧰 Also
- Slate Companion's log now says, for each copy of Savage Wind, which version it found (and where from), what Savage Wind last set up, and whether it needs setting up, so a problem like this is quick to see.
- `--sw-list` on the command line prints the same for every copy.

> [!NOTE]
> Tested on the Ubuntu PC where it happened: the copy now shows as set up. Written by Claude through Claude Code and directed and tested by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
