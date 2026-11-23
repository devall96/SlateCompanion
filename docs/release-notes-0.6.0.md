## Slate Companion 0.6.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.6.0/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.6.0/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.5.x or 0.4.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**The live map gets a camera cone, day and night, and the game's clock. Plus: Live Link is coming soon, and here's how to turn it on.**

> [!IMPORTANT]
> **Live Link needs the next Savage Wind release.** Link's live position comes from Savage Wind's read-only *companion socket*, which is on Savage Wind's `test` branch but not in a release yet (Alpha 6 and older don't have it). Until then, **Test** under *Live position* says "not connected" whatever you set. Sorry for shipping it before the game side was out, and for not saying so! ([#2](https://github.com/devall96/SlateCompanion/issues/2))
>
> **Once your Savage Wind has it:** on Savage Wind's start screen (Savage Wind Configuration), turn on **Companion apps** (it's saved, so once only). In Slate Companion, open **Saves & settings → Live position (Savage Wind)**, keep port **7777**, load a save and press **Test**. For another port, start Savage Wind with `SAVAGEWIND_COMPANION=<port>` and set the same port here. No administrator rights needed.

### 🗺️ New on the map (with Live Link)
- **Where the camera looks:** a cone in front of Link's marker turns with the game's camera.
- **Day and night:** the map darkens at night and warms at dawn and dusk, following the game's clock. Map icons stay bright and easy to read.
- **The game's time** in the top-right corner of the map, with the part of the day (Dawn, Morning, Afternoon, Evening, Dusk, Night).
- The cone and day/night each have a switch in the map's *Position* panel. Nothing extra is asked of the game: all of it comes from the same once-a-second position request as before.

### 🛠️ Fixed
- Settings and the "not connected" message told you to turn on a "Slate Companion" switch in Savage Wind. Savage Wind calls it **Companion apps**, and now Slate Companion does too.

> [!NOTE]
> Tested on Windows against a stand-in for Savage Wind's companion socket in demo mode: the cone turning with the camera, and the night tint at 23:00. The clock badge and the dawn and dusk colours were built but not checked on screen. Not yet tested against a real Savage Wind, since no release has the socket yet. The Linux build is checked by GitHub's test machine. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
