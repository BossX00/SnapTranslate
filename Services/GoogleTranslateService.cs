using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SnapTranslate.Models;

namespace SnapTranslate.Services;

public sealed class GoogleTranslateService
{
    private const int MaxCacheEntries = 256;

    private static readonly HttpClient SharedHttpClient = CreateClient();

    private sealed class EndpointEntry
    {
        public required string Name { get; init; }
        public required string UrlTemplate { get; init; }
        public required bool IsTranslateAT { get; init; }
        public DateTime CooldownUntilUtc { get; set; } = DateTime.MinValue;
    }

    private readonly EndpointEntry[] _endpoints =
    {
        new()
        {
            Name = "clients5-dict",
            UrlTemplate = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={0}&tl={1}",
            IsTranslateAT = true
        },
        new()
        {
            Name = "google-at",
            UrlTemplate = "https://translate.google.com/translate_a/single?client=at&dt=t&sl={0}&tl={1}",
            IsTranslateAT = false
        },
        new()
        {
            Name = "google-tw-ob",
            UrlTemplate = "https://translate.googleapis.com/translate_a/single?client=tw-ob&dt=t&sl={0}&tl={1}",
            IsTranslateAT = false
        },
        new()
        {
            Name = "google-gtx",
            UrlTemplate = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl={0}&tl={1}",
            IsTranslateAT = false
        }
    };

    private readonly object _cooldownLock = new();
    private readonly ConcurrentDictionary<string, TranslationResult> _cache = new();

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

        string cacheKey = $"{sourceLang}|{targetLang}|{text}";
        if (_cache.TryGetValue(cacheKey, out TranslationResult? cached))
        {
            return cached;
        }

        List<EndpointEntry> orderedEndpoints = GetOrderedEndpoints();
        Exception? lastException = null;
        bool sawRateLimit = false;

        foreach (EndpointEntry endpoint in orderedEndpoints)
        {
            ct.ThrowIfCancellationRequested();

            string url = string.Format(
                endpoint.UrlTemplate,
                Uri.EscapeDataString(sourceLang),
                Uri.EscapeDataString(targetLang));

            try
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("q", text)
                });

                using HttpResponseMessage response = await SharedHttpClient.PostAsync(url, content, ct).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    sawRateLimit = true;
                    SetCooldown(endpoint, TimeSpan.FromMinutes(3));
                    lastException = new HttpRequestException($"Google Translate HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    continue;
                }

                if ((int)response.StatusCode >= 500)
                {
                    SetCooldown(endpoint, TimeSpan.FromSeconds(30));
                    lastException = new HttpRequestException($"Google Translate HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    SetCooldown(endpoint, TimeSpan.FromSeconds(30));
                    lastException = new HttpRequestException($"Google Translate HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    continue;
                }

                TranslationResult result = endpoint.IsTranslateAT
                    ? ParseTranslateAT(body, text, sourceLang, targetLang)
                    : ParseTranslateASingle(body, text, sourceLang, targetLang);

                ClearCooldown(endpoint);
                StoreInCache(cacheKey, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetCooldown(endpoint, TimeSpan.FromSeconds(30));
                lastException = ex;
            }
        }

        if (sawRateLimit)
        {
            throw new HttpRequestException("Google Translate จำกัดจำนวนคำขอชั่วคราว (HTTP 429) กรุณารอสักครู่แล้วลองใหม่", lastException);
        }

        throw lastException ?? new InvalidOperationException("ไม่สามารถเชื่อมต่อบริการแปลภาษาได้");
    }

    internal static TranslationResult ParseTranslateAT(
        string body,
        string originalText,
        string sourceLang,
        string targetLang)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("รูปแบบข้อมูลตอบกลับจาก Google Translate (t) ไม่ถูกต้อง");
        }

        var translatedBuilder = new StringBuilder();
        string detectedSource = sourceLang;

        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                translatedBuilder.Append(item.GetString());
            }
            else if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
            {
                if (item[0].ValueKind == JsonValueKind.String)
                {
                    translatedBuilder.Append(item[0].GetString());
                }

                if (item.GetArrayLength() > 1 && item[1].ValueKind == JsonValueKind.String)
                {
                    string? lang = item[1].GetString();
                    if (!string.IsNullOrWhiteSpace(lang))
                    {
                        detectedSource = lang;
                    }
                }
            }
        }

        string translatedText = translatedBuilder.ToString();
        if (string.IsNullOrEmpty(translatedText))
        {
            throw new InvalidOperationException("ไม่พบผลลัพธ์การแปลจาก Google Translate");
        }

        return new TranslationResult(originalText, translatedText, detectedSource, targetLang);
    }

    internal static TranslationResult ParseTranslateASingle(
        string body,
        string originalText,
        string sourceLang,
        string targetLang)
    {
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

        return new TranslationResult(originalText, translatedText, detectedSource, targetLang);
    }

    private List<EndpointEntry> GetOrderedEndpoints()
    {
        DateTime now = DateTime.UtcNow;
        lock (_cooldownLock)
        {
            var ready = new List<EndpointEntry>(_endpoints.Length);
            var cooling = new List<EndpointEntry>(_endpoints.Length);

            foreach (EndpointEntry ep in _endpoints)
            {
                if (ep.CooldownUntilUtc <= now)
                {
                    ready.Add(ep);
                }
                else
                {
                    cooling.Add(ep);
                }
            }

            cooling.Sort((a, b) => a.CooldownUntilUtc.CompareTo(b.CooldownUntilUtc));
            ready.AddRange(cooling);
            return ready;
        }
    }

    private void SetCooldown(EndpointEntry endpoint, TimeSpan duration)
    {
        lock (_cooldownLock)
        {
            endpoint.CooldownUntilUtc = DateTime.UtcNow.Add(duration);
        }
    }

    private void ClearCooldown(EndpointEntry endpoint)
    {
        lock (_cooldownLock)
        {
            endpoint.CooldownUntilUtc = DateTime.MinValue;
        }
    }

    private void StoreInCache(string key, TranslationResult result)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            _cache.Clear();
        }

        _cache[key] = result;
    }
}
