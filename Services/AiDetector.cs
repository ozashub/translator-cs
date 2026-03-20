namespace Translator.Services;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

sealed class AiDetector : IDisposable
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    const string Endpoint = "https://api.zerogpt.com/api/detect/detectText";

    public string? LastError { get; private set; }

    public async Task<string?> Check(string text)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("input_text", text);
            w.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(ms.ToArray());
        var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Origin", "https://www.zerogpt.com");
        req.Headers.Add("Referer", "https://www.zerogpt.com/");
        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        try
        {
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"ZeroGPT HTTP {(int)resp.StatusCode}";
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var data = doc.RootElement.GetProperty("data");

            var pct = data.GetProperty("fakePercentage").GetDouble();
            var result = $"{pct:F1}% AI";

            if (data.TryGetProperty("feedback", out var fb))
            {
                var msg = fb.GetString();
                if (!string.IsNullOrWhiteSpace(msg))
                    result += $"\n{msg}";
            }

            LastError = null;
            return result;
        }
        catch (TaskCanceledException)
        {
            LastError = "ZeroGPT request timed out";
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"ZeroGPT: {ex.Message}";
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
