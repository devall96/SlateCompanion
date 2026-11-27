## Slate Companion 0.6.2 (hotfix)

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.6.2/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.6.2/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.6.x, 0.5.x or 0.4.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Hotfix: two reported bugs. Search no longer shows "Something went wrong" while you type, and the Clear Skies Update button now really updates Clear Skies.**

### 🛠️ Fixed
- **Search (Ctrl+K) could show "Something went wrong" while typing** ([#4](https://github.com/devall96/SlateCompanion/issues/4)). Each letter replaces the list of results, and while the old rows were cleared one of them was built again with nothing in it. That row is now simply left empty.
- **The Clear Skies Update button only opened the version you already had** ([#3](https://github.com/devall96/SlateCompanion/issues/3)). It relied on Clear Skies updating itself, which Clear Skies 0.2 can't do. Slate Companion now does the update:
  - It downloads the new Clear Skies and checks it against the size and checksum GitHub lists for it. If they don't match, nothing is changed.
  - **Windows:** Clear Skies' files are replaced in its own folder. Your `clearskies.ini` and anything else you added there are kept, and the files it replaces are copied to a `previous-<version>` folder first.
  - 🐧 **Linux:** the new AppImage is put next to the one you have and used from then on. The old one stays where it is.
  - Close Savage Wind and Clear Skies first. If one of the files is in use, Slate Companion says so and changes nothing.

> [!NOTE]
> Tested on Windows: updating a copy of Clear Skies 0.2.1 to 0.2.3 (files identical to the 0.2.3 download, settings and added files kept, old files in `previous-0.2.1`), a file held open (refused, nothing changed), and the AppImage download with its checksum. The AppImage step wasn't run on Linux itself, and the search fix comes from the crash report in #4: it wasn't reproduced on screen. The Linux build is checked by GitHub's test machine. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
