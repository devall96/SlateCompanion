## Slate Companion 0.6.1 (hotfix)

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.6.1/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.6.1/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.6.0 or 0.5.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Hotfix: Clear Skies looks show again when HDR is on in Savage Wind 0.6.** Nothing else changed.

### 🛠️ Fixed (Windows and Linux)
- **With HDR on in Savage Wind 0.6, the Clear Skies look you chose didn't show** ([ClearSkies#1](https://github.com/devall96/ClearSkies/issues/1), [ClearSkies#2](https://github.com/devall96/ClearSkies/issues/2)). Savage Wind 0.6 added its own HDR tone-mapping: with HDR on (and Auto turns it on on an HDR screen), it draws the picture with its own shader instead of the game's. Clear Skies works by turning the game's shader into Clarity, so the look was loaded (its log said so) but never drawn.
- **Now, when Play starts the game through Clear Skies with a look chosen, the game keeps its own tone-mapping**, which Clear Skies turns into Clarity, and Savage Wind still shows it in HDR. With the look set to **Off**, Savage Wind's own HDR tone-mapping is left alone.
- With a look and HDR on, you get Clarity's picture at Savage Wind's game brightness, without Savage Wind's extra-bright HDR highlights. If you'd rather have those, set the look to **Off**.

The same fix is in **Clear Skies 0.2.2**, out today, for players who start Clear Skies on its own. Slate Companion's Play screen offers that update too; this version already works with older Clear Skies.

> [!NOTE]
> **Why this keeps happening, and what's next.** Clear Skies swaps one specific shader inside the game, so changes to how Savage Wind draws the picture can break it, as HDR in 0.6 just did. I'm working on a more permanent fix so new Savage Wind versions stop breaking the looks.

> [!NOTE]
> Tested on Windows with Savage Wind 0.6.0-alpha.6 (AMD Radeon RX 9070 XT): with Windows HDR on, the problem was reproduced (the look barely showed) and then fixed (Serfrost's preset shows in HDR); with HDR off, looks still show as before. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
