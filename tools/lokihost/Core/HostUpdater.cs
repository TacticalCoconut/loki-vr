using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace LokiHost.Core;

/// <summary>
/// Optional, non-destructive GitHub update checker, applier, and rollback helper for Loki Host &amp; server installations.
/// Checks a user-configured GitHub repository (<see cref="HostSettings.UpdateGitHubRepo"/>) for new releases
/// without ever overwriting the user's database, <c>lokihost.json</c>, <c>HostDir</c> custom configs, or <c>loki.json</c>.
/// </summary>
public static class HostUpdater
{
    public sealed record ReleaseCheckResult(
        bool UpdateAvailable,
        uint RemoteVersion,
        string Commit,
        string DownloadUrl,
        string Sha256,
        string Summary);

    public sealed record RollbackPointInfo(
        bool Available,
        string BackupDirectory,
        string PreviousCommit,
        string UpdatedToCommit,
        string TimestampUtc);

    public static async Task<ReleaseCheckResult> CheckGitHubAsync(string githubRepo, uint currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(githubRepo))
            return new(false, currentVersion, "", "", "", "No GitHub repository configured for updates.");

        string repo = githubRepo.Trim().Trim('/');
        string manifestUrl = $"https://github.com/{repo}/releases/download/latest/version.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LokiHost-Updater/1.0");
        try
        {
            string json = await http.GetStringAsync(manifestUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            uint ver = root.TryGetProperty("version", out var vEl) && vEl.TryGetUInt32(out uint v) ? v : 0;
            string commit = root.TryGetProperty("commit", out var cEl) ? cEl.GetString() ?? "" : "";
            string url = root.TryGetProperty("downloadUrl", out var uEl) ? uEl.GetString() ?? "" : "";
            string sha = root.TryGetProperty("sha256", out var sEl) ? sEl.GetString() ?? "" : "";

            if (ver > currentVersion && !string.IsNullOrEmpty(url))
                return new(true, ver, commit, url, sha, $"Update v{ver} ({commit}) is available on {repo}.");

            return new(false, ver, commit, url, sha, $"Up to date (v{Math.Max(currentVersion, ver)}).");
        }
        catch (Exception ex)
        {
            return new(false, currentVersion, "", "", "", $"Update check skipped: {ex.Message}");
        }
    }

    public static RollbackPointInfo GetLatestRollbackPoint(string repoRoot)
    {
        try
        {
            string backupsRoot = Path.Combine(repoRoot, "backups");
            if (!Directory.Exists(backupsRoot))
                return new(false, "", "", "", "");

            var dirs = Directory.GetDirectories(backupsRoot, "pre-update-*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);

            foreach (string dir in dirs)
            {
                string metaFile = Path.Combine(dir, "backup-meta.json");
                if (!File.Exists(metaFile)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(metaFile));
                var root = doc.RootElement;
                string prev = root.TryGetProperty("previousCommit", out var pEl) ? pEl.GetString() ?? "" : "";
                string next = root.TryGetProperty("updatedToCommit", out var uEl) ? uEl.GetString() ?? "" : "";
                string ts = root.TryGetProperty("timestampUtc", out var tEl) ? tEl.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(prev))
                    return new(true, dir, prev, next, ts);
            }
        }
        catch { }
        return new(false, "", "", "", "");
    }

    public static bool RollbackLatestUpdate(string repoRoot, out string summary)
    {
        var rb = GetLatestRollbackPoint(repoRoot);
        if (!rb.Available)
        {
            summary = "No rollback backup point is available.";
            return false;
        }

        string scriptPath = Path.Combine(repoRoot, "tools", "update-loki-vr.ps1");
        if (!File.Exists(scriptPath))
        {
            summary = "tools/update-loki-vr.ps1 was not found.";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -Rollback -BackupDir \"{rb.BackupDirectory}\"",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            summary = "Failed to start PowerShell rollback process.";
            return false;
        }
        string output = proc.StandardOutput.ReadToEnd();
        string err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode == 0)
        {
            summary = $"Rolled back to commit {rb.PreviousCommit}.";
            return true;
        }
        summary = $"Rollback failed: {(string.IsNullOrWhiteSpace(err) ? output : err).Trim()}";
        return false;
    }
}
