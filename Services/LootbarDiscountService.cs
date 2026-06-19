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
/// Response format: {"data":{"items":[{"app_service_id":226,"appid":20170,"save_price_rate":12},...]},"status":"ok"}
/// Cache key = app_service_id. Map qua LootbarAppServiceId trong appsettings.json.
/// </summary>
public class LootbarDiscountService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LootbarDiscountService> _logger;

    // cache: app_service_id → discount%
    private readonly ConcurrentDictionary<int, int> _cache = new();

    private const string SaveRateUrlTemplate =
        "https://api.lootbar.com/api/v2/market/shop/game_app_service/save_rate" +
        "?service_type=recharge&page_num=1&page_size=50&incoming=1" +
        "&shop_code={0}&utm_source=Affiliate&utm_medium=shop&utm_campaign={0}";

    public LootbarDiscountService(
        IHttpClientFactory httpClientFactory,
        ILogger<LootbarDiscountService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task WarmCacheAsync(string shopCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shopCode))
        {
            _logger.LogWarning("LootbarDiscountService.WarmCacheAsync: shopCode trống — bỏ qua");
            return;
        }

        var url = string.Format(SaveRateUrlTemplate, shopCode);

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(LootbarDiscountService));
            var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Lootbar save_rate HTTP {Status} — shopCode={Code}",
                    (int)response.StatusCode, shopCode);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            // Debug only — tránh dump raw API data vào production logs
            _logger.LogDebug("Lootbar save_rate raw (shopCode={Code}): {Preview}",
                shopCode, body.Length > 500 ? body[..500] : body);

            ParseAndCache(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("LootbarDiscountService.WarmCacheAsync cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootbarDiscountService.WarmCacheAsync thất bại (shopCode={Code})", shopCode);
        }
    }

    /// <summary>Lấy discount theo app_service_id.</summary>
    public int? GetDiscount(int appServiceId)
        => _cache.TryGetValue(appServiceId, out var v) ? v : null;

    // ── Parser ───────────────────────────────────────────────────────────────

    private void ParseAndCache(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!TryGetList(root, out var list))
            {
                _logger.LogWarning("Lootbar save_rate: không tìm thấy items array trong response.");
                return;
            }

            var count = 0;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("app_service_id", out var idEl) ||
                    !idEl.TryGetInt32(out var appServiceId)) continue;

                var pct = 0;
                if (item.TryGetProperty("save_price_rate", out var rateEl) &&
                    rateEl.TryGetInt32(out var rate))
                    pct = rate;

                _cache[appServiceId] = pct;
                _logger.LogDebug("Lootbar cached: app_service_id={Id} → {Pct}%", appServiceId, pct);
                count++;
            }

            _logger.LogInformation("Lootbar WarmCache done — {Count} services cached", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lootbar ParseAndCache thất bại");
        }
    }

    private static bool TryGetList(JsonElement root, out JsonElement list)
    {
        if (root.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("items", out list) && list.ValueKind == JsonValueKind.Array)
                return true;
            if (data.TryGetProperty("list", out list) && list.ValueKind == JsonValueKind.Array)
                return true;
            if (data.TryGetProperty("records", out list) && list.ValueKind == JsonValueKind.Array)
                return true;
            if (data.ValueKind == JsonValueKind.Array) { list = data; return true; }
        }
        if (root.TryGetProperty("list", out list) && list.ValueKind == JsonValueKind.Array)
            return true;

        list = default;
        return false;
    }
}
