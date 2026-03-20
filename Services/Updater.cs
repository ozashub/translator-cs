namespace Translator.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

static class Updater
{
    const string Repo = "ozashub/translator-cs";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public record Release(string Tag, string SetupUrl, long Size);

    static Updater()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Translator");
    }

    public static string GetCurrentVersion()
    {
        return typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public static string? LastCheckError { get; private set; }

    public static async Task<Release?> CheckAsync()
    {
        try
        {
            LastCheckError = null;
            var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
            {
                LastCheckError = $"GitHub HTTP {(int)resp.StatusCode}";
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
            if (tag == null) { LastCheckError = "no tag"; return null; }

            var current = GetCurrentVersion();
            if (tag == current) { LastCheckError = "up to date"; return null; }

            if (!Version.TryParse(tag, out var remote) || !Version.TryParse(current, out var local))
            { LastCheckError = "bad version format"; return null; }
            if (remote <= local) { LastCheckError = "up to date"; return null; }

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name == null) continue;
                if (!name.Contains("Setup") || !name.EndsWith(".exe")) continue;

                return new Release(
                    tag,
                    asset.GetProperty("browser_download_url").GetString()!,
                    asset.GetProperty("size").GetInt64()
                );
            }

            LastCheckError = "no Setup.exe in release";
        }
        catch (Exception ex)
        {
            LastCheckError = ex.Message;
        }
        return null;
    }

    public static async Task<string?> DownloadAndRun(Release release, IProgress<(double pct, string status)> progress)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "translator-update");

        try
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var setupPath = Path.Combine(tmp, "TranslatorSetup.exe");

            progress.Report((0, $"Downloading v{release.Tag}\u2026"));

            using var resp = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? release.Size;

            using (var stream = await resp.Content.ReadAsStreamAsync())
            using (var fs = File.Create(setupPath))
            {
                var buf = new byte[81920];
                long done = 0;
                while (true)
                {
                    var n = await stream.ReadAsync(buf);
                    if (n == 0) break;
                    await fs.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    var pct = total > 0 ? (double)done / total * 95 : 0;
                    var mb = done / 1048576.0;
                    var totalMb = total / 1048576.0;
                    progress.Report((pct, $"Downloading v{release.Tag}  {mb:F1} / {totalMb:F1} MB"));
                }
            }

            progress.Report((98, "Launching installer\u2026"));
            Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
