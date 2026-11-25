using System.Net.Http;
using System.Text;
using System.Text.Json;
using SnapTranslate.Models;

namespace SnapTranslate.Services;

public sealed class GoogleTranslateService
{
    private static readonly HttpClient SharedHttpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        return client;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("ไม่มีข้อความสำหรับแปล", nameof(text));
        }

        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}&dt=t";

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", text)
        });

        using HttpResponseMessage response = await SharedHttpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google Translate HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("รูปแบบข้อมูลตอบกลับจาก Google Translate ไม่ถูกต้อง");
        }

        var translatedBuilder = new StringBuilder();
        foreach (JsonElement segment in root[0].EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.Array &&
                segment.GetArrayLength() > 0 &&
                segment[0].ValueKind == JsonValueKind.String)
            {
                translatedBuilder.Append(segment[0].GetString());
            }
        }

        string translatedText = translatedBuilder.ToString();
        if (string.IsNullOrEmpty(translatedText))
        {
            throw new InvalidOperationException("ไม่พบผลลัพธ์การแปลจาก Google Translate");
        }

        string detectedSource = sourceLang;
        if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
        {
            string? lang = root[2].GetString();
            if (!string.IsNullOrWhiteSpace(lang))
            {
                detectedSource = lang;
            }
        }

        return new TranslationResult(text, translatedText, detectedSource, targetLang);
    }
}
