## Slate Companion 0.4.1 (hotfix)

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.4.1/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.4.1/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.4.0 or 0.3.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**Hotfix for 0.4.0: the Zelda Dungeon tab no longer opens browser tabs by itself.** Nothing else changed; if you're on 0.4.0, especially on Linux, please update.

### 🌐 Zelda Dungeon tab
- 🐧 **No more browser tabs opening by themselves (Linux).** The tab sent every page load that wasn't on zeldadungeon.net to your browser, and on Linux that includes every ad inside the page, so Firefox kept filling up with tabs. With Savage Wind running it also kept closing and reopening the tab, which opened more.
- **The tab now never opens anything by itself**: no pop-up windows, no browser tabs, no other programs. Your browser opens only when you press *Open in your browser*. Pop-ups are refused (one for a Zelda Dungeon page opens in the tab instead).
- **If you click your way off Zelda Dungeon**, a bar offers *Back to Zelda Dungeon* or *Open in your browser*.

New in 0.4.0, in case you missed it: the walkthrough ticks itself from your save, and every chapter, quest and shrine has *Read on Zelda Dungeon*. See the [0.4.0 notes](https://github.com/devall96/SlateCompanion/releases/tag/v0.4.0).

> [!NOTE]
> Tested on Windows: the tab on Zelda Dungeon's pages starts no browser and no window of its own. The Linux build is checked to start by GitHub's test machine; this fix hasn't been tried on Linux hardware yet. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
