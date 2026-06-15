using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Gọi API Lootbar để lấy % giảm giá cho tất cả game trong affiliate shop.
///
/// Endpoint: GET https://api.lootbar.com/api/v2/market/shop/game_app_service/save_rate
///   ?service_type=recharge&page_num=1&page_size=50&incoming=1
///   &shop_code={shopCode}&utm_source=Affiliate&utm_medium=shop&utm_campaign={shopCode}
///
/// Lần đầu chạy, log "save_rate raw" sẽ hiển thị cấu trúc JSON thực tế.
/// Nếu field names khác với dưới đây, cập nhật ParseItem().
/// </summary>
public class LootbarDiscountService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LootbarDiscountService> _logger;

    // cache: LootbarGameSeo → discount%
    private readonly ConcurrentDictionary<string, int> _cache = new(StringComparer.OrdinalIgnoreCase);

    private const string SaveRateUrlTemplate =
        "https://api.lootbar.com/api/v2/market/shop/game_app_service/save_rate" +
        "?service_type=recharge&page_num=1&page_size=50&incoming=1" +
        "&shop_code={0}&utm_source=Affiliate&utm_medium=shop&utm_campaign={0}";

    public LootbarDiscountService(IHttpClientFactory httpClientFactory, ILogger<LootbarDiscountService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task WarmCacheAsync(string shopCode)
    {
        if (string.IsNullOrWhiteSpace(shopCode)) return;

        var url = string.Format(SaveRateUrlTemplate, shopCode);

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(LootbarDiscountService));
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Lootbar save_rate HTTP {Status} — {Url}",
                    (int)response.StatusCode, url);
                return;
            }

            var body = await response.Content.ReadAsStringAsync();

            // Log toàn bộ response lần đầu để xác nhận field names
            _logger.LogInformation("Lootbar save_rate raw (shopCode={Code}): {Body}",
                shopCode, body.Length > 2000 ? body[..2000] : body);

            ParseAndCache(body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootbarDiscountService.WarmCacheAsync thất bại (shopCode={Code})", shopCode);
        }
    }

    public int? GetDiscount(string gameSeo)
        => _cache.TryGetValue(gameSeo, out var v) ? v : null;

    // ── Parser ───────────────────────────────────────────────────────────

    private void ParseAndCache(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Tìm array items — thử các key thông dụng
            if (!TryGetList(root, out var list))
            {
                _logger.LogWarning("Lootbar save_rate: không tìm thấy list trong response. Xem log 'raw' ở trên để kiểm tra cấu trúc JSON.");
                return;
            }

            var count = 0;
            foreach (var item in list.EnumerateArray())
            {
                var (seo, pct) = ParseItem(item);
                if (seo is null || pct <= 0) continue;
                _cache[seo] = pct;
                _logger.LogInformation("Lootbar cached: {Seo} → {Pct}%", seo, pct);
                count++;
            }

            _logger.LogInformation("Lootbar WarmCache done — {Count} games cached", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lootbar ParseAndCache thất bại");
        }
    }

    /// <summary>
    /// Thử các cấu trúc response phổ biến của Lootbar.
    /// Nếu sai, xem log "save_rate raw" và cập nhật hàm này.
    /// </summary>
    private static bool TryGetList(JsonElement root, out JsonElement list)
    {
        // {"data": {"list": [...]}}
        if (root.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("list", out list) && list.ValueKind == JsonValueKind.Array)
                return true;
            // {"data": {"records": [...]}}
            if (data.TryGetProperty("records", out list) && list.ValueKind == JsonValueKind.Array)
                return true;
            // {"data": [...]}
            if (data.ValueKind == JsonValueKind.Array) { list = data; return true; }
        }
        // {"list": [...]}
        if (root.TryGetProperty("list", out list) && list.ValueKind == JsonValueKind.Array)
            return true;
        list = default;
        return false;
    }

    /// <summary>
    /// Parse một item trong array → (gameSeo, discountPercent).
    /// TODO: Cập nhật field names dựa theo log "save_rate raw" lần đầu chạy.
    /// Các field name thường gặp: game_seo / gameSeo / seo_name / slug
    ///                             save_rate / saveRate / discount / discount_rate / rate
    /// </summary>
    private (string? seo, int pct) ParseItem(JsonElement item)
    {
        // ── Game identifier ────────────────────────────────────────
        string? seo = null;
        foreach (var key in new[] { "game_seo", "gameSeo", "seo_name", "slug", "game_slug" })
        {
            if (item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                seo = v.GetString();
                if (!string.IsNullOrEmpty(seo)) break;
            }
        }

        // ── Discount percent ───────────────────────────────────────
        var pct = 0;
        // Thử string field: "15%" hoặc "15"
        foreach (var key in new[] { "save_rate", "saveRate", "discount_rate", "discount", "rate", "save_rate_str" })
        {
            if (item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                pct = ParsePercent(v.GetString());
                if (pct > 0) break;
            }
        }
        // Thử int/number field
        if (pct == 0)
        {
            foreach (var key in new[] { "save_rate_num", "saveRateNum", "discount_num", "discountNum", "save_rate" })
            {
                if (item.TryGetProperty(key, out var v) &&
                    (v.ValueKind == JsonValueKind.Number) &&
                    v.TryGetInt32(out var n) && n > 0)
                {
                    pct = n;
                    break;
                }
            }
        }

        return (seo, pct);
    }

    private static int ParsePercent(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var clean = value.TrimEnd('%', ' ');
        return int.TryParse(clean, out var n) ? Math.Abs(n) : 0;
    }
}
