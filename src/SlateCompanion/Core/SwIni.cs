namespace SlateCompanion;

/// <summary>
/// Savage Wind's savagewind.ini (key = value lines, as runtime/settings.c reads and writes them). Changes only the
/// keys asked for, in place, keeps every other line and the file's line endings, writes through a temporary file, and
/// never while a Savage Wind is running (it writes the whole file back when its settings change).
/// </summary>
public sealed class SwIni
{
    readonly string path;
    readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public bool Exists { get; }

    public SwIni(string path)
    {
        this.path = path;
        Exists = File.Exists(path);
        if (!Exists) return;
        try
        {
            foreach (var line in File.ReadAllLines(path))
                if (Split(line) is { } kv) values[kv.key] = kv.value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static (string key, string value)? Split(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || t[0] is '#' or ';' or '[') return null;
        int eq = t.IndexOf('=');
        if (eq <= 0) return null;
        return (t[..eq].Trim(), t[(eq + 1)..].Trim());
    }

    public string? Get(string key) => values.GetValueOrDefault(key);
    public bool Has(string key) => values.ContainsKey(key);

    /// <summary>Writes these keys (others untouched). Returns null, or why it wasn't written.</summary>
    public string? Set(IReadOnlyDictionary<string, string> changes)
    {
        if (Launcher.GameRunning()) return "Savage Wind is running. Change settings once it's closed (it saves its own settings when it quits).";
        try
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            string nl = text.Contains("\r\n") ? "\r\n" : OperatingSystem.IsWindows() && text.Length == 0 ? "\r\n" : "\n";
            var lines = text.Length == 0 ? new List<string>() : text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
            var left = new Dictionary<string, string>(changes);
            for (int i = 0; i < lines.Count; i++)
                if (Split(lines[i]) is { } kv && left.Remove(kv.key, out var v))
                    lines[i] = $"{kv.key} = {v}";
            foreach (var (k, v) in left) lines.Add($"{k} = {v}");
            var tmp = path + ".slate.tmp";
            File.WriteAllText(tmp, string.Join(nl, lines) + nl);
            File.Move(tmp, path, true);
            foreach (var (k, v) in changes) values[k] = v;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    // ---------------------------------------------------------------- launch options (launch-flags.txt, NAME=value)

    public static string ReadLaunchFlags(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n").Trim() : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Saves NAME=value lines (only SAVAGEWIND_* names, as Savage Wind accepts); empty removes the file.</summary>
    public static string? WriteLaunchFlags(string file, string text)
    {
        if (Launcher.GameRunning()) return "Savage Wind is running. Change launch options once it's closed.";
        var good = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;
            int eq = l.IndexOf('=');
            var name = (eq < 0 ? l : l[..eq]).Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^SAVAGEWIND_[A-Z0-9_]+$")) return $"\"{name}\" isn't a Savage Wind launch option (they start with SAVAGEWIND_).";
            good.Add(eq < 0 ? name + "=1" : name + "=" + l[(eq + 1)..].Trim());
        }
        try
        {
            if (good.Count == 0) { if (File.Exists(file)) File.Delete(file); return null; }
            File.WriteAllText(file, string.Join("\n", good) + "\n");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }
}
