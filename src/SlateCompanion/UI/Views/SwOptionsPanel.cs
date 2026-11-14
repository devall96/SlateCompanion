using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace SlateCompanion.UI.Views;

/// <summary>
/// Every setting of Savage Wind Configuration (Data/sw-options.json, taken from Savage Wind's own option rows and
/// settings.c), read from and written to the chosen copy's savagewind.ini: same tabs, names, choices and descriptions.
/// A change is written at once (only that key), and never while Savage Wind runs.
/// </summary>
public static class SwOptionsPanel
{
    static JsonObject? spec;
    static int lastTab;
    static readonly string Os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static JsonObject? Spec()
    {
        if (spec != null) return spec;
        try { spec = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "sw-options.json")))!.AsObject(); }
        catch (Exception e) { Log.Error("sw-options.json", e); }
        return spec;
    }

    public static Control Build(SwInstall? sw, bool running)
    {
        var head = Ui.Stack(4, Ui.Text("SAVAGE WIND SETTINGS", "h2"));
        if (sw == null) return Ui.Panel(Ui.Stack(8, head, Ui.Text("Choose a Savage Wind to change its settings.", "muted small")));
        var data = Spec();
        if (data == null) return Ui.Panel(Ui.Stack(8, head, Ui.Text("The settings list is missing from this copy of Slate Companion.", "muted small")));

        var ini = new SwIni(sw.Ini);
        var caps = GpuCaps.For(sw);  // (what this PC's graphics card can't do is greyed out)
        head.Children.Add(Ui.Text(running
            ? "Savage Wind is running: these show its saved settings, and can be changed once it's closed. (It saves its own settings when it quits.)"
            : ini.Exists ? $"The same settings as Savage Wind Configuration. A change is saved at once to savagewind.ini ({Ui.ShortPath(sw.DataRoot)}) and applies the next time the game starts."
                         : "Savage Wind hasn't saved its settings yet. Anything you change here is saved for its first start.", "muted small"));
        ((TextBlock)head.Children[^1]).TextWrapping = TextWrapping.Wrap;

        var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Ui.Brush("Muted"), Text = "Point at a setting to see what it does." };
        var descBox = new Border { Classes = { "note" }, Child = desc, MinHeight = 74 };
        var refreshers = new List<Action>();
        bool quiet = false;
        void RefreshAll() { quiet = true; try { foreach (var r in refreshers) r(); } finally { quiet = false; } }

        string Val(JsonNode row, string key) => ini.Get(key) ?? (row["key"]?.GetValue<string>() == key ? row["default"]?.GetValue<string>() : null) ?? "";
        void Write(Dictionary<string, string> set, string what)
        {
            if (quiet) return;
            var err = ini.Set(set);
            if (err != null) { ErrorWindow.Show("Setting not saved", $"{what} couldn't be saved: {err}"); }
            else Log.Info($"Savage Wind setting ({Ui.ShortPath(sw.Ini)}): {string.Join(", ", set.Select(kv => kv.Key + " = " + kv.Value))}");
            RefreshAll();
        }

        bool Cond(string? c)
        {
            if (string.IsNullOrEmpty(c)) return true;
            if (c.StartsWith("a shipped release", StringComparison.Ordinal)) return sw.Version != null;
            if (c.StartsWith("compatible game code", StringComparison.Ordinal)) return false;  // (only for CPUs without AVX2, which Savage Wind decides itself)
            bool not = c.Contains("!=");
            var parts = c.Split(not ? "!=" : "=", 2);
            if (parts.Length != 2) return true;
            var have = ini.Get(parts[0].Trim()) ?? "";
            return not ? have != parts[1].Trim() : have == parts[1].Trim();
        }

        var tabs = new TabControl { Padding = new Thickness(0, 8, 0, 0) };
        foreach (var tab in data["tabs"]!.AsArray())
        {
            var page = Ui.Stack(4);
            foreach (var sec in tab!["sections"]!.AsArray())
            {
                var rows = sec!["rows"]!.AsArray().Where(r => r!["platforms"] is not JsonArray p || p.Any(x => x!.GetValue<string>() == Os)).ToList();
                if (rows.Count == 0) continue;
                if (sec["title"]?.GetValue<string>() is { } st) { var h = Ui.Text(st.ToUpperInvariant(), "faint small"); h.Margin = new Thickness(0, 10, 0, 2); h.LetterSpacing = 2; page.Children.Add(h); }
                foreach (var row in rows) page.Children.Add(Row(row!));
            }
            if (tab["id"]!.GetValue<string>() == "gamefiles") page.Children.Add(LaunchOptions(sw, running, desc));
            tabs.Items.Add(new TabItem { Header = new TextBlock { Text = tab["title"]!.GetValue<string>(), FontSize = 14 }, Content = page });
        }
        tabs.SelectedIndex = Math.Clamp(lastTab, 0, tabs.ItemCount - 1);
        tabs.SelectionChanged += (_, _) => lastTab = tabs.SelectedIndex;
        RefreshAll();
        if (caps.Gpu != null)
        {
            var note = Ui.Text($"Graphics card: {caps.Gpu}. Choices it can't use are greyed out (from {caps.Source}).", "faint small");
            note.TextWrapping = TextWrapping.Wrap;
            head.Children.Add(note);
        }
        var body = Ui.Stack(10, head, descBox, tabs);
        body.IsEnabled = true;
        tabs.IsEnabled = !running;
        return Ui.Panel(body);

        // ---------------------------------------------------------------- one row
        Control Row(JsonNode row)
        {
            string label = row["label"]!.GetValue<string>(), kind = row["kind"]!.GetValue<string>();
            string? key = row["key"]?.GetValue<string>();
            var text = Clean(row["desc"]?.GetValue<string>() ?? "");
            Control editor;
            Action refresh;

            if (row["multi"] is JsonArray multi)
            {
                var labels = multi.Select(m => m!["label"]!.GetValue<string>()).ToList();
                string? custom = row["custom"]?.GetValue<string>();
                int Current()
                {
                    if (label == "Savage Wind colour look") return ini.Get("colour_lut") == "sw:Savage_Wind.cube" ? labels.IndexOf("On") : labels.IndexOf("Off");
                    for (int i = 0; i < multi.Count; i++)
                        if (multi[i]!["set"]!.AsObject().All(kv => (ini.Get(kv.Key) ?? "") == kv.Value!.GetValue<string>())) return i;
                    return -1;
                }
                if (kind == "toggle")
                {
                    var ts = new ToggleSwitch { OnContent = "", OffContent = "" };
                    ts.IsCheckedChanged += (_, _) => Write(Set(multi[labels.IndexOf(ts.IsChecked == true ? "On" : "Off")]!), label);
                    editor = ts;
                    refresh = () => ts.IsChecked = Current() == labels.IndexOf("On");
                }
                else
                {
                    var items = new List<string>(labels);
                    if (custom != null) items.Add(custom);
                    var cb = new ComboBox { ItemsSource = items, MinWidth = 190 };
                    cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0 && cb.SelectedIndex < multi.Count) Write(Set(multi[cb.SelectedIndex]!), label); };
                    editor = cb;
                    refresh = () => { int i = Current(); cb.SelectedIndex = i >= 0 ? i : custom != null ? items.Count - 1 : -1; };
                }
            }
            else if (key == null) { editor = Ui.Text("—", "faint"); refresh = () => { }; }
            else if (kind == "toggle")
            {
                var ts = new ToggleSwitch { OnContent = "", OffContent = "" };
                ts.IsCheckedChanged += (_, _) => Write(new() { [key] = ts.IsChecked == true ? "1" : "0" }, label);
                editor = ts;
                refresh = () => ts.IsChecked = Val(row, key) is "1" or "true" or "on";
            }
            else if (kind is "slider" or "int")
            {
                double min = row["min"]!.GetValue<double>(), max = row["max"]!.GetValue<double>(), step = row["step"]?.GetValue<double>() ?? 1, scale = row["scale"]?.GetValue<double>() ?? 1;
                var fmt = row["format"]?.GetValue<string>() ?? "%g";
                var sl = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, Width = 190 };
                var shown = Ui.Text("", "mono small"); shown.MinWidth = 64; shown.TextAlignment = TextAlignment.Right;
                sl.PropertyChanged += (_, e) =>
                {
                    if (e.Property != RangeBase.ValueProperty) return;
                    shown.Text = Format(fmt, sl.Value);
                    if (!quiet) pending = () => Write(new() { [key] = (kind == "int" ? Math.Round(sl.Value * scale) : Math.Round(sl.Value * scale, 4)).ToString(kind == "int" ? "0" : "0.####", Inv) }, label);
                };
                // written when the slider is let go (or after a key press), not on every step of a drag
                sl.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, _) => Flush(), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
                sl.KeyUp += (_, _) => Flush();
                sl.LostFocus += (_, _) => Flush();
                editor = Ui.Row(8, sl, shown);
                refresh = () =>
                {
                    double v = double.TryParse(Val(row, key), NumberStyles.Float, Inv, out var d) ? d / scale : min;
                    sl.Value = Math.Clamp(v, min, max);
                    shown.Text = Format(fmt, sl.Value);
                };
            }
            else // choice
            {
                var choices = (row["choices"] as JsonArray)?.Select(c => (label: c!["label"]!.GetValue<string>(), value: c["value"]!.GetValue<string>())).ToList() ?? [];
                choices = Dynamic(key, choices);
                var cb = new ComboBox { MinWidth = 190, MaxDropDownHeight = 420 };
                List<(string label, string value)> shownChoices = choices;
                cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0 && cb.SelectedIndex < shownChoices.Count) Write(new() { [key] = shownChoices[cb.SelectedIndex].value }, label); };
                editor = cb;
                refresh = () =>
                {
                    var v = Val(row, key);
                    shownChoices = choices;
                    if (v != "" && !choices.Any(c => c.value == v)) shownChoices = [.. choices, (v.Contains(' ') && key == "display" ? v[(v.IndexOf(' ') + 1)..] : v, v)];
                    cb.ItemsSource = shownChoices.Select(c =>
                    {
                        var why = caps.WhyNot(key, c.value);
                        return new ComboBoxItem { Content = why == null ? c.label : $"{c.label}: {why}", IsEnabled = why == null, Opacity = why == null ? 1 : 0.45 };
                    }).ToList();
                    cb.SelectedIndex = shownChoices.FindIndex(c => c.value == v);
                };
            }

            var name = Ui.Text(label, "small"); name.VerticalAlignment = VerticalAlignment.Center; name.TextWrapping = TextWrapping.Wrap;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(row["indent"]?.GetValue<int>() > 0 ? 18 : 0, 0, 0, 0), Background = Brushes.Transparent, MinHeight = 36 };
            g.Children.Add(name);
            editor.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(editor, 1); g.Children.Add(editor);
            var border = new Border { Child = g, Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(6) };
            border.PointerEntered += (_, _) => { desc.Text = label + ": " + text; border.Background = new SolidColorBrush(Color.Parse("#1439C6FF")); };
            border.PointerExited += (_, _) => border.Background = null;
            string? showIf = row["showIf"]?.GetValue<string>(), enabledIf = row["enabledIf"]?.GetValue<string>();
            refreshers.Add(() => { refresh(); border.IsVisible = Cond(showIf); g.IsEnabled = Cond(enabledIf); });
            return border;
        }

        static Dictionary<string, string> Set(JsonNode m) => m["set"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());

        List<(string label, string value)> Dynamic(string key, List<(string label, string value)> given)
        {
            var list = new List<(string, string)>(given);
            if (key == "window_size")
            {
                list.Insert(0, ("Default", "default"));
                foreach (var (w, h, shape) in new[] { (1280, 720, "16:9"), (1600, 900, "16:9"), (1920, 1080, "16:9"), (2560, 1440, "16:9"), (3840, 2160, "16:9"), (2560, 1080, "21:9"), (3440, 1440, "21:9"), (3840, 1600, "21:9"), (1280, 800, "16:10"), (1680, 1050, "16:10"), (1920, 1200, "16:10"), (2560, 1600, "16:10"), (1024, 768, "4:3"), (1600, 1200, "4:3") })
                    list.Add(($"{w} x {h}  ({shape})", $"{w}x{h}"));
            }
            else if (key == "colour_lut")
            {
                foreach (var dir in new[] { Path.Combine(sw.Folder, "colour-luts"), Path.Combine(sw.DataRoot, "luts") })
                {
                    if (!Directory.Exists(dir)) continue;
                    bool shipped = dir.EndsWith("colour-luts", StringComparison.Ordinal);
                    try
                    {
                        foreach (var f in Directory.GetFiles(dir).Where(f => f.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f))
                        {
                            var value = (shipped ? "sw:" : "") + Path.GetFileName(f);
                            if (!list.Any(x => x.Item2 == value)) list.Add((Path.GetFileNameWithoutExtension(f).Replace('_', ' '), value));
                        }
                    }
                    catch (Exception) { }
                }
            }
            return list;
        }

        void Flush() { var p = pending; pending = null; p?.Invoke(); }
    }

    static Action? pending;

    /// <summary>Savage Wind's launch options (launch-flags.txt): SAVAGEWIND_NAME=value, one per line.</summary>
    static Control LaunchOptions(SwInstall sw, bool running, TextBlock desc)
    {
        var box = new TextBox { Text = SwIni.ReadLaunchFlags(sw.LaunchFlags), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 90, FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, monospace"), FontSize = 12, PlaceholderText = "SAVAGEWIND_NAME=value, one per line" };
        var save = Ui.Button("Save launch options", () =>
        {
            var err = SwIni.WriteLaunchFlags(sw.LaunchFlags, box.Text ?? "");
            if (err != null) ErrorWindow.Show("Launch options not saved", err);
            else { Log.Info("Savage Wind launch options saved"); App.Main.Toast("Launch options saved"); }
        }, "small");
        save.IsEnabled = !running;
        var s = Ui.Stack(6, Ui.Text("LAUNCH OPTIONS", "faint small"), box, save);
        s.Margin = new Thickness(0, 12, 0, 0);
        s.PointerEntered += (_, _) => desc.Text = "Launch options: settings a developer may ask you to try, as NAME=value lines (they start with SAVAGEWIND_). Saved in launch-flags.txt; Savage Wind applies them at its next start. Empty it to go back to normal.";
        return s;
    }

    static string Clean(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"%[-+ #0]*\d*(\.\d+)?[dsfgx%]", m => m.Value == "%%" ? "%" : "…").Replace("  ", " ").Trim();

    /// <summary>A C printf format ("%g%%", "%+.1f EV", "%+d", "%d%%") for one number.</summary>
    static string Format(string fmt, double v) =>
        System.Text.RegularExpressions.Regex.Replace(fmt, @"%([-+ #0]*)(\d*)(?:\.(\d+))?([dsfg%])", m =>
        {
            if (m.Groups[4].Value == "%") return "%";
            bool plus = m.Groups[1].Value.Contains('+');
            string num = m.Groups[4].Value switch
            {
                "d" => Math.Round(v).ToString("0", Inv),
                "f" => v.ToString("F" + (m.Groups[3].Success ? m.Groups[3].Value : "6"), Inv),
                _ => Math.Round(v, 4).ToString("0.####", Inv),
            };
            return plus && v >= 0 ? "+" + num : num;
        });
}
