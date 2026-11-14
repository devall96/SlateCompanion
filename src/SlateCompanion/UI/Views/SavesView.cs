using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace SlateCompanion.UI.Views;

/// <summary>
/// Save files: the slots of each Savage Wind copy and save profile (picture, date, play time), backups, importing a
/// save (a Wii U / Cemu one is converted, as Savage Wind's Saves tab does), exporting (for Savage Wind or for Cemu),
/// copying a slot elsewhere and deleting one. Every change backs the whole save folder up first, into Savage Wind's
/// own backup folder, and none runs while the game is open.
/// </summary>
public sealed class SavesView : ViewBase
{
    readonly ContentControl head = new(), slots = new(), backups = new(), import = new();
    List<SwInstall> installs = [];
    List<SaveTarget> targets = [];
    SaveTarget? target;
    SaveManager.Incoming? incoming;
    bool busy;

    protected override Control Build()
    {
        var page = Ui.Stack(16, Ui.Header("Save files"), head, import, slots, backups);
        return new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = page } };
    }

    protected override void OnShown(object? arg) => Refresh();

    string NameOf(SwInstall i) => App.Progress.Settings.InstallNames.GetValueOrDefault(i.Exe) ?? i.Label;

    void Refresh()
    {
        installs = Launcher.FindInstalls(App.Progress.Settings).Where(i => Directory.Exists(i.DataRoot)).ToList();
        var keep = target;
        var sw = installs.FirstOrDefault(i => i.Exe == keep?.Install.Exe) ?? installs.FirstOrDefault(i => i.Exe == App.Progress.Settings.GameProgram)
                 ?? installs.FirstOrDefault(i => SaveManager.Targets(i).Any(t => SaveManager.Slots(t).Count > 0)) ?? installs.FirstOrDefault();
        targets = sw != null ? SaveManager.Targets(sw) : [];
        target = targets.FirstOrDefault(t => t.Profile == keep?.Profile) ?? targets.FirstOrDefault(t => t.InUse) ?? targets.FirstOrDefault();
        Draw();
    }

    void Draw() { DrawHead(); DrawImport(); DrawSlots(); DrawBackups(); }

    void DrawHead()
    {
        var s = Ui.Stack(10);
        var note = Ui.Text("Changing saves always backs the whole save folder up first (Savage Wind's own backups, which its Saves tab can restore too), and waits until the game is closed. Your progress tracking only ever reads saves.", "muted small");
        note.TextWrapping = TextWrapping.Wrap;
        s.Children.Add(note);
        if (installs.Count == 0 || target == null) { s.Children.Add(Ui.Text("No Savage Wind on this computer yet.", "muted")); head.Content = Ui.Panel(s); return; }
        var copy = new ComboBox { ItemsSource = installs.Select(NameOf).ToList(), SelectedIndex = Math.Max(0, installs.FindIndex(i => i.Exe == target.Install.Exe)), MinWidth = 380 };
        copy.SelectionChanged += (_, _) =>
        {
            if (copy.SelectedIndex < 0 || installs[copy.SelectedIndex].Exe == target?.Install.Exe) return;
            targets = SaveManager.Targets(installs[copy.SelectedIndex]);
            target = targets.FirstOrDefault(t => t.InUse) ?? targets.FirstOrDefault();
            Draw();
        };
        var profile = new ComboBox { ItemsSource = targets.Select(t => t.Label).ToList(), SelectedIndex = Math.Max(0, targets.IndexOf(target)), MinWidth = 200 };
        profile.SelectionChanged += (_, _) => { if (profile.SelectedIndex >= 0 && targets[profile.SelectedIndex] != target) { target = targets[profile.SelectedIndex]; DrawImport(); DrawSlots(); DrawBackups(); } };
        s.Children.Add(Ui.Row(10, Ui.Text("Savage Wind", "faint small"), copy, Ui.Text("Profile", "faint small"), profile));
        var bar = new WrapPanel();
        void B(string text, Action a, string cls = "small") { var b = Ui.Button(text, a, cls); b.Margin = new Thickness(0, 4, 8, 0); b.IsEnabled = !busy; bar.Children.Add(b); }
        B("Back up now", () => Do(t => SaveManager.Backup(t) is { } p ? $"Backed up to {Path.GetFileName(p)}" : "There were no saves to back up."), "small primary");
        B("Import a save…", PickImport);
        B("Export (.zip)", () => ExportZip(false, null));
        B("Export for Cemu (.zip)", () => ExportZip(true, null), "small ghost");
        B("Open save folder", () => ErrorWindow.OpenPath(target.SaveDir), "small ghost");
        s.Children.Add(bar);
        head.Content = Ui.Panel(s, "panel glow");
    }

    // ---------------------------------------------------------------- import

    async void PickImport()
    {
        var picked = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a save: a .zip, or a game_data.sav inside its slot folder (Cemu, Ryujinx, Yuzu, Switch or Savage Wind)",
            FileTypeFilter = [new FilePickerFileType("Breath of the Wild saves") { Patterns = ["*.zip", "*.sav"] }, FilePickerFileTypes.All],
        });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) Inspect(path);
    }

    void Inspect(string path)
    {
        try
        {
            if (incoming != null) SaveManager.Cleanup(incoming);
            incoming = SaveManager.Inspect(path);
            DrawImport();
        }
        catch (SaveManager.SaveException e) { ErrorWindow.Show(e.Title, e.Message); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { ErrorWindow.Show("That save can't be read", e.Message, e); }
    }

    void DrawImport()
    {
        var s = Ui.Stack(10, Ui.Text("IMPORT", "h2"));
        if (incoming == null)
        {
            var others = App.Progress.Sources.Where(x => x.Kind != "savagewind").ToList();
            if (others.Count > 0)
            {
                s.Children.Add(Ui.Text("Found on this computer:", "faint small"));
                foreach (var o in others)
                    s.Children.Add(Ui.Row(10, Ui.Tag(o.Kind == "cemu" ? "Cemu (Wii U)" : o.Label, o.Kind == "cemu" ? "orange" : "blue"), Ui.Text(Ui.ShortPath(o.Folder), "faint small mono"), Ui.Button("Import…", () => Inspect(o.Folder), "small")));
            }
            else s.Children.Add(Ui.Text("Import a save… takes a Cemu (Wii U) save, a Switch or emulator save, or a .zip of one.", "muted small"));
            import.Content = Ui.Panel(s);
            return;
        }
        var inc = incoming;
        s.Children.Add(Ui.Row(10, Ui.Tag(inc.WiiU ? "Wii U save (Cemu)" : "Switch save", inc.WiiU ? "orange" : "blue"), Ui.Text($"{inc.Slots} slot{(inc.Slots == 1 ? "" : "s")} · {Ui.ShortPath(inc.Root)}", "small")));
        if (inc.WiiU) s.Children.Add(Ui.Text("It's converted for Savage Wind on the way in: the same conversion as Savage Wind's Saves tab, using your own game files.", "muted small"));
        var all = Ui.Button($"Replace all saves of {target?.Label} with it", async () =>
        {
            if (!await Confirm("Replace all saves?", $"All saves of {target!.Label} are backed up first, then replaced by this save (every slot, options, Hero's Path and album).", "Back up and replace")) return;
            Do(t => SaveManager.ImportAll(t, inc).LastOrDefault() ?? "Imported", after: () => incoming = null);
        }, "small primary");
        var fromSlots = Directory.GetDirectories(inc.Root).Select(d => Path.GetFileName(d)!).Where(n => n.All(char.IsAsciiDigit) && File.Exists(Path.Combine(inc.Root, n, "game_data.sav"))).OrderBy(int.Parse).ToList();
        var from = new ComboBox { ItemsSource = fromSlots.Select(n => "its slot " + n).ToList(), SelectedIndex = 0 };
        var to = new ComboBox { ItemsSource = Enumerable.Range(0, 6).Select(n => "into slot " + n).ToList(), SelectedIndex = 0 };
        var one = Ui.Button("Import that slot", async () =>
        {
            var f = fromSlots[Math.Max(0, from.SelectedIndex)]; var tsl = Math.Max(0, to.SelectedIndex).ToString();
            if (!await Confirm("Import one slot?", $"All saves of {target!.Label} are backed up first, then slot {tsl} is replaced by slot {f} of this save.", "Back up and import")) return;
            Do(t => { SaveManager.ImportSlot(t, inc, f, tsl); return $"Slot {f} imported into slot {tsl}"; }, after: () => incoming = null);
        }, "small");
        s.Children.Add(all);
        s.Children.Add(Ui.Row(8, Ui.Text("Or just one:", "faint small"), from, to, one, Ui.Button("Cancel", () => { SaveManager.Cleanup(inc); incoming = null; DrawImport(); }, "small ghost")));
        import.Content = Ui.Panel(s, "panel glow");
    }

    // ---------------------------------------------------------------- slots

    void DrawSlots()
    {
        if (target == null) { slots.Content = null; return; }
        var list = SaveManager.Slots(target);
        var s = Ui.Stack(10, Ui.Text($"SLOTS · {target.Label.ToUpperInvariant()}", "h2"));
        if (list.Count == 0) { s.Children.Add(Ui.Text("No saves in this profile yet.", "muted small")); slots.Content = Ui.Panel(s); return; }
        var wrap = new WrapPanel();
        foreach (var sl in list)
        {
            Bitmap? pic = null;
            if (sl.Picture != null) try { using var f = File.OpenRead(sl.Picture); pic = Bitmap.DecodeToWidth(f, 320); } catch (Exception) { }
            var info = Ui.Stack(4, Ui.Text($"Slot {sl.Name}", "h3"), Ui.Text($"Saved {sl.Saved:g}" + (sl.PlaySeconds is { } p ? $" · played {Ui.Duration(p)}" : ""), "muted small"));
            var actions = new WrapPanel();
            void B(string text, Action a) { var b = Ui.Button(text, a, "small ghost"); b.Margin = new Thickness(0, 4, 6, 0); b.IsEnabled = !busy; actions.Children.Add(b); }
            B("Export", () => ExportZip(false, sl.Name));
            B("Copy to…", () => CopyTo(sl.Name));
            B("Delete", async () =>
            {
                if (!await Confirm($"Delete slot {sl.Name}?", "All saves of this profile are backed up first, then this slot is removed.", "Back up and delete")) return;
                Do(t => { SaveManager.DeleteSlot(t, sl.Name); return $"Slot {sl.Name} deleted (it's in the backup)"; });
            });
            info.Children.Add(actions);
            var card = Ui.Panel(Ui.StackOf(8, Ui.Picture(pic, 300), info), "panel");
            card.Width = 334; card.Margin = new Thickness(0, 0, 12, 12);
            wrap.Children.Add(card);
        }
        s.Children.Add(wrap);
        slots.Content = Ui.Panel(s);
    }

    async void CopyTo(string slot)
    {
        var all = installs.SelectMany(SaveManager.Targets).ToList();
        var where = new ComboBox { ItemsSource = all.Select(t => $"{NameOf(t.Install)} · {t.Label}").ToList(), SelectedIndex = Math.Max(0, all.FindIndex(t => t.SaveDir == target!.SaveDir)), MinWidth = 460 };
        var to = new ComboBox { ItemsSource = Enumerable.Range(0, 6).Select(n => "slot " + n).ToList(), SelectedIndex = 0 };
        var w = Dialog($"Copy slot {slot}", Ui.Stack(10, Ui.Text("Where to", "faint small"), where, to, Ui.Text("The saves there are backed up first; that slot is replaced.", "muted small")), "Back up and copy");
        if (!await w.ShowDialog<bool>(App.Main) || where.SelectedIndex < 0) return;
        var dest = all[where.SelectedIndex]; var tsl = Math.Max(0, to.SelectedIndex).ToString(); var src = target!;
        Do(_ => { SaveManager.CopySlot(src, slot, dest, tsl); return $"Slot {slot} copied to slot {tsl} of {dest.Label}"; });
    }

    // ---------------------------------------------------------------- backups

    void DrawBackups()
    {
        if (target == null) { backups.Content = null; return; }
        var t0 = target;
        var list = SaveManager.Backups(t0);
        var s = Ui.Stack(8, Ui.Text("BACKUPS", "h2"));
        if (list.Count == 0) s.Children.Add(Ui.Text("No backups yet. One is made before every change, or with Back up now.", "muted small"));
        foreach (var (path, when) in list.Take(30))
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            g.Children.Add(Ui.Stack(2, Ui.Text(Path.GetFileName(path), "small mono"), Ui.Text($"{when:g} · {SaveLocator.ListSlots(Path.Combine(path, "save_0")).Count} slots", "faint small")));
            var r = Ui.Row(6, Ui.Button("Restore", async () =>
            {
                if (!await Confirm("Restore this backup?", $"The saves of {t0.Label} now are backed up first, then replaced by {Path.GetFileName(path)} (which stays).", "Back up and restore")) return;
                Do(t => { SaveManager.Restore(t, path); return $"{Path.GetFileName(path)} restored"; });
            }, "small"), Ui.Button("Open", () => ErrorWindow.OpenPath(path), "small ghost"));
            r.IsEnabled = !busy;
            Grid.SetColumn(r, 1); g.Children.Add(r);
            s.Children.Add(g);
        }
        backups.Content = Ui.Panel(s);
    }

    // ---------------------------------------------------------------- export

    async void ExportZip(bool cemu, string? slot)
    {
        if (target == null) return;
        var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = cemu ? "Save the export for Cemu (unpack it into Cemu's user/80000001 folder)" : "Save the export",
            SuggestedFileName = $"BotW save{(slot != null ? " slot " + slot : "")}{(cemu ? " (Wii U)" : "")} {DateTime.Now:yyyy-MM-dd}.zip",
            DefaultExtension = "zip",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var t = target;
        busy = true; Draw();
        try { await Task.Run(() => SaveManager.Export(t, path, cemu, slot)); App.Main.Toast("Exported to " + Path.GetFileName(path)); }
        catch (SaveManager.SaveException e) { ErrorWindow.Show(e.Title, e.Message); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { ErrorWindow.Show("Export failed", e.Message, e); }
        busy = false; Draw();
    }

    // ---------------------------------------------------------------- helpers

    async void Do(Func<SaveTarget, string> work, Action? after = null)
    {
        if (target == null) return;
        var t = target;
        busy = true; Draw();
        try
        {
            App.Main.Toast(await Task.Run(() => work(t)));
            after?.Invoke();
            _ = App.Progress.RefreshAsync();
        }
        catch (SaveManager.SaveException e) { ErrorWindow.Show(e.Title, e.Message); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { ErrorWindow.Show("Something went wrong", e.Message + " Your saves were backed up before anything changed.", e); }
        busy = false;
        Refresh();
    }

    static Window Dialog(string title, Control body, string okText)
    {
        var w = new Window { Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Brush("Bg"), MaxWidth = 620 };
        var ok = Ui.Button(okText, () => w.Close(true), "primary"); ok.IsDefault = true;
        var cancel = Ui.Button("Cancel", () => w.Close(false), "ghost"); cancel.IsCancel = true;
        w.Content = new Border { Padding = new Thickness(22), Child = Ui.Stack(14, Ui.Text(title, "h1"), body, Ui.Row(8, ok, cancel)) };
        return w;
    }

    static async Task<bool> Confirm(string title, string text, string okText)
    {
        var t = Ui.Text(text, "muted"); t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 540;
        return await Dialog(title, t, okText).ShowDialog<bool>(App.Main);
    }
}
