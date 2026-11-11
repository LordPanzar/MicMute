using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MicMute;

/// <summary>Checks GitHub Releases for a newer version and installs it with the release's setup file.</summary>
internal static class Updater
{
    public const string Repo = "LordPanzar/MicMute";
    public static string RepoUrl => "https://github.com/" + Repo;

    public sealed record Release(Version Version, string Tag, string Notes, string SetupUrl, string SetupName, long Size, string PageUrl);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MicMute-Updater", CurrentVersion.ToString(3)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static Version CurrentVersion
    {
        get
        {
            var s = Application.ProductVersion.Split('+')[0];
            return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
        }
    }

    /// <summary>Running from the installed copy (not a dev build)? Updates replace the installed files only.</summary>
    public static bool IsInstalled =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>Latest release, or null if none / no setup file attached. Throws on network errors.</summary>
    public static async Task<Release> GetLatestAsync()
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) continue;
            return new Release(ver, tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                a.GetProperty("browser_download_url").GetString(), name,
                a.GetProperty("size").GetInt64(),
                root.GetProperty("html_url").GetString());
        }
        return null;
    }

    public static bool IsNewer(Release r) => r != null && Normalize(r.Version) > Normalize(CurrentVersion);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Downloads the setup to %TEMP% (progress 0–1) and returns its path.</summary>
    public static async Task<string> DownloadAsync(Release r, IProgress<double> progress)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MicMute-update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Path.GetFileName(r.SetupName));
        using var resp = await Http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? r.Size;
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (total > 0 && new FileInfo(path).Length != total) throw new IOException("Nedlastingen ble ufullstendig.");
        return path;
    }

    /// <summary>Starts the setup silently; it waits for MicMute to exit, installs and starts the new version.</summary>
    public static void RunSetup(string path)
    {
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE") { UseShellExecute = true });
    }
}
