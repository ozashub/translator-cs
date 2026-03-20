namespace Translator.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

sealed class OpenAiClient : IDisposable
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    string _model = Prompts.Model;

    public string? LastError { get; private set; }

    public void Configure(string apiKey, string? model = null)
    {
        if (model != null) _model = model;
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<string?> Chat(string system, string user, double temp = 0.7, string? model = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model ?? _model);
            w.WriteNumber("temperature", temp);
            w.WriteStartArray("messages");
            w.WriteStartObject(); w.WriteString("role", "system"); w.WriteString("content", system); w.WriteEndObject();
            w.WriteStartObject(); w.WriteString("role", "user"); w.WriteString("content", user); w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }

        var content = new StringContent(Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8, "application/json");

        try
        {
            var resp = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                try
                {
                    using var doc = JsonDocument.Parse(errBody);
                    LastError = doc.RootElement
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString() ?? $"HTTP {(int)resp.StatusCode}";
                }
                catch
                {
                    LastError = $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}";
                }
                return null;
            }

            using var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            LastError = null;
            return json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim();
        }
        catch (TaskCanceledException)
        {
            LastError = "Request timed out (60s)";
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastError = $"Network error: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
