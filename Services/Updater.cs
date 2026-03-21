namespace Translator.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

static class Updater
{
    const string Repo = "ozashub/translator-cs";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public record Release(string Tag, string ZipUrl, long Size);

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
            if (!Version.TryParse(tag, out var remote) || !Version.TryParse(current, out var local))
            { LastCheckError = "bad version format"; return null; }
            if (remote <= local) { LastCheckError = "up to date"; return null; }

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name == null) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("arm", StringComparison.OrdinalIgnoreCase)) continue;

                return new Release(
                    tag,
                    asset.GetProperty("browser_download_url").GetString()!,
                    asset.GetProperty("size").GetInt64()
                );
            }

            LastCheckError = "no portable zip in release";
        }
        catch (Exception ex)
        {
            LastCheckError = ex.Message;
        }
        return null;
    }

    public static async Task<string?> DownloadAndApply(Release release, IProgress<(double pct, string status)> progress)
    {
        var staging = Path.Combine(Path.GetTempPath(), "translator-update");
        var zipPath = Path.Combine(Path.GetTempPath(), "translator-update.zip");

        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            progress.Report((0, $"Downloading v{release.Tag}\u2026"));

            using var resp = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? release.Size;

            await using (var net = await resp.Content.ReadAsStreamAsync())
            await using (var fs = File.Create(zipPath))
            {
                var buf = new byte[81920];
                long done = 0;
                while (true)
                {
                    var n = await net.ReadAsync(buf);
                    if (n == 0) break;
                    await fs.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    var pct = total > 0 ? (double)done / total * 80 : 0;
                    var mb = done / 1048576.0;
                    var totalMb = total / 1048576.0;
                    progress.Report((pct, $"Downloading v{release.Tag}  {mb:F1}/{totalMb:F1} MB"));
                }
            }

            progress.Report((82, "Extracting\u2026"));
            ZipFile.ExtractToDirectory(zipPath, staging, true);
            File.Delete(zipPath);

            progress.Report((90, "Applying update\u2026"));
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var exe = Environment.ProcessPath ?? Path.Combine(appDir, "Translator.exe");

            var script = Path.Combine(Path.GetTempPath(), "translator-update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"taskkill /F /IM Translator.exe >nul 2>&1\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                $"xcopy /s /y /q \"{staging}\\*\" \"{appDir}\\\"\r\n" +
                $"rd /s /q \"{staging}\"\r\n" +
                $"start \"\" \"{exe}\"\r\n" +
                $"del \"%~f0\"\r\n");

            progress.Report((95, "Restarting\u2026"));
            Process.Start(new ProcessStartInfo(script)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });

            return null;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            return ex.Message;
        }
    }
}
