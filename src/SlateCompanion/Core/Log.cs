using System.Runtime.InteropServices;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// The app's log (logs/slatecompanion.log in its folder; the previous run's is kept as slatecompanion.1.log) and crash
/// reports (logs/crash-&lt;time&gt;.txt). Never holds save contents or anything sent anywhere: it stays on this computer
/// until the player chooses to attach it to a report.
/// </summary>
public static class Log
{
    public static string Folder { get; } = Path.Combine(Store.DefaultFolder(), "logs");
    public static string FilePath => Path.Combine(Folder, "slatecompanion.log");
    static readonly Lock gate = new();
    static readonly Queue<string> recent = new();
    static StreamWriter? writer;
    const long MaxBytes = 2 << 20;

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath)) File.Move(FilePath, Path.Combine(Folder, "slatecompanion.1.log"), true);
            writer = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            // old crash reports: the newest 10 are kept
            foreach (var f in Directory.GetFiles(Folder, "crash-*.txt").OrderByDescending(f => f).Skip(10)) File.Delete(f);
        }
        catch (Exception) { writer = null; }
        Info($"Slate Companion {Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
    }

    public static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static void Info(string msg) => Write("info", msg);
    public static void Warn(string msg) => Write("warn", msg);
    public static void Error(string msg, Exception? e = null) => Write("ERROR", e == null ? msg : $"{msg}: {e}");

    static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Scrub(msg)}";
        lock (gate)
        {
            recent.Enqueue(line);
            while (recent.Count > 300) recent.Dequeue();
            try
            {
                if (writer != null && writer.BaseStream.Length < MaxBytes) writer.WriteLine(line);
            }
            catch (Exception) { }
        }
        if (level != "info") Console.Error.WriteLine(line);
    }

    /// <summary>The user's home folder as ~, so a log can be shared without the user name in every path.</summary>
    public static string Scrub(string s)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 3 ? s.Replace(home, "~", StringComparison.OrdinalIgnoreCase) : s;
    }

    /// <summary>Writes a crash report (the error, the app and system, the last log lines). Returns its path, or null.</summary>
    public static string? Crash(Exception e, string where)
    {
        Error($"Crash ({where})", e);
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Slate Companion {Version} crash report, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Where: {where}");
            sb.AppendLine($"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
            sb.AppendLine();
            sb.AppendLine(Scrub(e.ToString()));
            sb.AppendLine();
            sb.AppendLine("Last log lines:");
            lock (gate) foreach (var l in recent) sb.AppendLine(l);
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, sb.ToString());
            return file;
        }
        catch (Exception) { return null; }
    }

    public static string Recent()
    {
        lock (gate) return string.Join(Environment.NewLine, recent);
    }

    /// <summary>A message box from the system, for a crash before (or after) the window is up.</summary>
    public static void NativeAlert(string title, string text)
    {
        try
        {
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, text, title, 0x10 /* MB_ICONERROR */);
            else
            {
                foreach (var (tool, args) in new[] { ("zenity", new[] { "--error", "--title", title, "--text", text }), ("kdialog", new[] { "--title", title, "--error", text }) })
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(tool) { UseShellExecute = false };
                        foreach (var a in args) psi.ArgumentList.Add(a);
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit(120_000);
                        return;
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
