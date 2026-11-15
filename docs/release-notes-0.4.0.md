## Slate Companion 0.4.0

> [!TIP]
> ### ⬇️ Downloads: these are the only files you need
> Easiest: **[devall96.github.io/SlateCompanion](https://devall96.github.io/SlateCompanion/)** picks the right one for your PC.
>
> - **Windows:** [**SlateCompanion-win-Setup.exe**](https://github.com/devall96/SlateCompanion/releases/download/v0.4.0/SlateCompanion-win-Setup.exe) (installer, no admin needed)
> - **Linux:** [**SlateCompanion.AppImage**](https://github.com/devall96/SlateCompanion/releases/download/v0.4.0/SlateCompanion.AppImage) (make it executable, then run it)
>
> Already on 0.3.x? Slate Companion offers this update when it opens.
>
> Everything else under *Assets* (`.nupkg`, `.json`, `RELEASES`) is read by the built-in updater: you can ignore it.

**New: the walkthrough follows your save, and Zelda Dungeon's guide is one click away from every chapter, quest and shrine.**

### 📖 A walkthrough that ticks itself
- **Every walkthrough step has its own tick, and most tick themselves from your save**: shrines cleared, towers activated, towns reached, quests started or finished, Divine Beasts entered and freed, and the DLC quests. A chapter ticks once all its steps have.
- **Tick from my save** (on the Quests & walkthrough screen, on by default): turning it on fills in everything your save has already done, all at once. Turning it off leaves only the ticks you made by hand.
- Steps the save can't prove (looking at your photo album, the walk up Hyrule Castle) stay yours to tick by hand, as before: blue is done in your save, green is ticked by hand.

### 🌐 Zelda Dungeon tab
- **A new tab with [Zelda Dungeon](https://www.zeldadungeon.net/)'s own guide**, shown inside Slate Companion as it is on their website. Nothing of theirs is copied into the app.
- **It never opens anything by itself**: no pop-up windows, no browser tabs, no other programs. Your browser opens only when you press *Open in your browser*. If you click your way off Zelda Dungeon, a bar offers *Back to Zelda Dungeon* or *Open in your browser*.
- **Read on Zelda Dungeon** on every walkthrough chapter (the Divine Beast chapters link both the town and the dungeon), all 15 main, 76 side and 42 shrine quests, and all 136 shrines. Each of the 284 links was opened and checked on their site.
- **It costs nothing when you're not using it.** The page only loads when you open the tab. Leave the tab or the window and it's closed 30 seconds later, web processes and all; minimize, or start Savage Wind, and it's closed at once. Coming back opens it again where you were, scrolled to the same spot. (Their page with its ads uses well over a gigabyte while it's open, which is why it isn't kept around.)
- 🐧 On Linux the tab uses WebKitGTK (usually the package `webkit2gtk-4.1` or `libwebkit2gtk-4.1`). Without it, the tab says so and offers to open the page in your browser.

> [!NOTE]
> Tested on Windows with real Savage Wind saves: the tracking at different points in a playthrough, the switch on and off, every screen, and the Zelda Dungeon tab's memory and CPU while open, in the background, minimized and back. The Linux build is checked to start by GitHub's test machine; the Zelda Dungeon tab hasn't been tried on Linux hardware yet. Written by Claude through Claude Code and directed by devall96. [Report a problem](https://github.com/devall96/SlateCompanion/issues).
