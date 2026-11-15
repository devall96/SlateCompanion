using Velopack;
using Velopack.Sources;

namespace SlateCompanion;

/// <summary>
/// Updates from this project's GitHub releases (Velopack): only when installed with the Setup / AppImage, and only
/// when asked from Settings. Nothing is downloaded until the player presses Update.
/// </summary>
public static class Updater
{
    public const string Repo = "https://github.com/devall96/SlateCompanion";
    static UpdateManager? manager;
    static UpdateInfo? pending;

    static UpdateManager Manager => manager ??= new UpdateManager(new GithubSource(Repo, null, false));

    public static bool Installed
    {
        get { try { return Manager.IsInstalled; } catch (Exception) { return false; } }
    }

    public static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>The newer version's number, or null when this is the newest.</summary>
    public static async Task<string?> CheckAsync()
    {
        pending = await Manager.CheckForUpdatesAsync();
        return pending?.TargetFullRelease.Version.ToString();
    }

    public static async Task DownloadAndRestartAsync(Action<int> progress)
    {
        if (pending == null) return;
        await Manager.DownloadUpdatesAsync(pending, progress);
        // restarting in the middle of a Savage Wind install or update (between two file swaps) could leave it broken
        if (UI.Views.SwJob.Running) throw new InvalidOperationException("Savage Wind is being installed or updated. Install the Slate Companion update once that has finished.");
        Manager.ApplyUpdatesAndRestart(pending);
    }
}
