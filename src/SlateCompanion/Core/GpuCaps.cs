namespace SlateCompanion;

/// <summary>
/// What this PC's graphics card can do among Savage Wind's settings, so the Play screen greys out what the player
/// can't use (DLSS on a non-NVIDIA card, FSR 4 without AMD's FSR 4…). Best source: what Savage Wind itself found
/// the last time this copy ran (savagewind.log: "[vk] using <card>", "[fsr] AMD FSR API: upscaler versions …",
/// "[dlss] Super Resolution available|unavailable …"); otherwise the card's name from the system, with the same
/// rules Savage Wind follows (DLSS: NVIDIA RTX; FSR 4: AMD Radeon on Windows with AMD's FSR 4).
/// </summary>
public sealed record GpuCaps(string? Gpu, bool? Dlss, bool? Fsr4, bool? Fsr3, string Source)
{
    /// <summary>Why a setting value can't be used here, or null when it can (or nobody can tell).</summary>
    public string? WhyNot(string key, string value)
    {
        if (key != "upscaler") return null;
        return value switch
        {
            "dlss" when Dlss == false => "needs an NVIDIA RTX graphics card" + (Gpu != null ? $" (this PC: {Gpu})" : ""),
            "fsr4" when Fsr4 == false => OperatingSystem.IsWindows() ? "needs an AMD Radeon RX 9000 card with AMD's FSR 4" + (Gpu != null ? $" (this PC: {Gpu})" : "") : "AMD FSR 4 is Windows only",
            "fsr3" when Fsr3 == false => "not offered by this copy of Savage Wind",
            "metalfx" when !OperatingSystem.IsMacOS() => "Mac only",
            _ => null,
        };
    }

    static readonly Dictionary<string, (DateTime, GpuCaps)> cache = [];

    public static GpuCaps For(SwInstall sw)
    {
        foreach (var name in new[] { "savagewind.log", "savagewind.prev.log" })
        {
            var log = Path.Combine(sw.DataRoot, name);
            if (!File.Exists(log)) continue;
            var when = File.GetLastWriteTimeUtc(log);
            if (cache.TryGetValue(log, out var c) && c.Item1 == when) return c.Item2;
            var caps = FromLog(log);
            if (caps == null) continue;
            cache[log] = (when, caps);
            return caps;
        }
        return FromSystem();
    }

    static GpuCaps? FromLog(string path)
    {
        string? gpu = null; bool? dlss = null, fsr4 = null, fsr3 = null;
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(f);
            for (int i = 0; i < 4000 && r.ReadLine() is { } line; i++)
            {
                if (gpu == null && line.StartsWith("[vk] using ", StringComparison.Ordinal)) gpu = line[11..].Trim();
                else if (line.StartsWith("[fsr] AMD FSR API: upscaler versions ", StringComparison.Ordinal))
                {
                    var v = line[37..];
                    fsr4 = v.Split(',').Any(x => x.Trim().StartsWith("4.", StringComparison.Ordinal));
                    fsr3 = true;
                }
                else if (line.StartsWith("[fsr] AMD FSR API: no upscaler", StringComparison.Ordinal)) fsr4 = false;
                else if (line.StartsWith("[dlss] Super Resolution ", StringComparison.Ordinal)) dlss = line.Contains(" available on", StringComparison.Ordinal);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        if (gpu == null) return null;  // (the game never got as far as the graphics card in that run)
        bool nvidia = gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || gpu.Contains("GeForce", StringComparison.OrdinalIgnoreCase);
        dlss ??= nvidia && gpu.Contains("RTX", StringComparison.OrdinalIgnoreCase) ? null : false;
        fsr4 ??= false;  // (Savage Wind found no FSR 4 with this card)
        return new GpuCaps(gpu, dlss, fsr4, fsr3, "Savage Wind's log");
    }

    static GpuCaps FromSystem()
    {
        var names = new List<string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (cls != null)
                    foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsAsciiDigit)))
                        using (var k = cls.OpenSubKey(sub))
                            if (k?.GetValue("DriverDesc") is string d) names.Add(d);
            }
            else if (Directory.Exists("/sys/class/drm"))
            {
                foreach (var card in Directory.GetDirectories("/sys/class/drm", "card?"))
                {
                    var v = Path.Combine(card, "device", "vendor");
                    if (!File.Exists(v)) continue;
                    names.Add(File.ReadAllText(v).Trim() switch { "0x10de" => "NVIDIA", "0x1002" => "AMD", "0x8086" => "Intel", var o => o });
                }
            }
        }
        catch (Exception) { }
        var gpu = names.FirstOrDefault(n => n.Contains("NVIDIA") || n.Contains("AMD") || n.Contains("Radeon")) ?? names.FirstOrDefault();
        if (gpu == null) return new GpuCaps(null, null, null, null, "unknown");
        bool nvidia = gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        bool amd = gpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        // the name alone: only what's certain is greyed (a card that isn't NVIDIA has no DLSS; not AMD or not Windows: no FSR 4)
        bool? dlss = nvidia ? (gpu.Contains("RTX", StringComparison.OrdinalIgnoreCase) ? null : false) : names.Count > 0 && !names.Any(n => n.Contains("NVIDIA")) ? false : null;
        bool? fsr4 = !OperatingSystem.IsWindows() ? false : amd ? null : false;
        return new GpuCaps(gpu, dlss, fsr4, null, "the graphics card's name");
    }
}
