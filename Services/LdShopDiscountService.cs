using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeDiscordBot.Services;

public class LdShopDiscountService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LdShopDiscountService> _logger;

    private const string SkuPageUrl = "https://api.ldshop.gg/api/commodity/v4/sku/page";

    private readonly ConcurrentDictionary<int, int> _cache = new();

    private static readonly string[] ExcludedSkuKeywords =
    [
        "New User Exclusive", "Bundle", "Guarantee", "Collection",
        "Outfit", "Subscription", "Upgrade", "Chassis", "Ring",
        "Aid", "Protocol", "Lightpack", "Heavypack", "Voyage",
        "Prep", "Insider Channel", "Connoisseur Channel",
    ];

    public LdShopDiscountService(
        IHttpClientFactory httpClientFactory,
        ILogger<LdShopDiscountService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Cache warm ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch discount cho tất cả game song song.
    /// Dùng Task.WhenAll thay vì foreach tuần tự — giảm thời gian warm
    /// từ N×RTT xuống còn max(RTT) khi có N game.
    /// </summary>
    public async Task WarmCacheAsync(
        IEnumerable<(int commodityId, int skuLabelId)> games,
        CancellationToken ct = default)
    {
        var tasks = games.Select(async g =>
        {
            var pct = await FetchDiscountAsync(g.commodityId, g.skuLabelId, ct);
            if (pct is > 0)
                _cache[g.commodityId] = (int)Math.Round(pct.Value);
        });

        await Task.WhenAll(tasks);
    }

    public int? GetDiscount(int commodityId)
        => _cache.TryGetValue(commodityId, out var v) ? v : null;

    // ── API fetch ────────────────────────────────────────────────────────────

    public async Task<double?> FetchDiscountAsync(
        int commodityId,
        int skuLabelId,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                page = new { current = 1, size = 100 },
                commodityId,
                skuLabelId
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var httpClient = _httpClientFactory.CreateClient(nameof(LdShopDiscountService));
            var response = await httpClient.PostAsync(SkuPageUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "HTTP {Status} from sku/page (commodityId={Id}) — {Body}",
                    (int)response.StatusCode, commodityId,
                    errorBody.Length > 200 ? errorBody[..200] : errorBody);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            // Chỉ log ở Debug — tránh dump API data vào production logs
            _logger.LogDebug("sku/page raw (commodityId={Id}): {Preview}",
                commodityId, body.Length > 300 ? body[..300] : body);

            var result = JsonSerializer.Deserialize<SkuPageResponse>(body);

            if (result?.Data is not { Count: > 0 })
            {
                _logger.LogWarning("Empty data — commodityId={Id}", commodityId);
                return null;
            }

            // Debug: log vài SKU mẫu để dễ debug khi cần
            foreach (var sku in result.Data.Take(3))
            {
                _logger.LogDebug(
                    "  SKU sample (commodityId={Id}) — name={Name}, discount={Discount}, " +
                    "stockStatus={Stock}, excluded={Excl}",
                    commodityId, sku.SkuName, sku.Discount, sku.StockStatus,
                    IsExcludedSku(sku.SkuName));
            }

            var discounts = result.Data
                .Where(x => x.StockStatus == 1
                         && !string.IsNullOrEmpty(x.Discount)
                         && x.Discount != "None"
                         && !IsExcludedSku(x.SkuName))
                .Select(x => ParseDiscountPercent(x.Discount))
                .Where(x => x > 0)
                .ToList();

            if (discounts.Count == 0)
            {
                _logger.LogInformation("No discount after filter — commodityId={Id}", commodityId);
                return 0;
            }

            var maxDiscount = discounts.Max();
            _logger.LogInformation("Discount fetched — commodityId={Id}, max={Pct}%",
                commodityId, maxDiscount);
            return maxDiscount;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("FetchDiscountAsync cancelled — commodityId={Id}", commodityId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchDiscountAsync failed — commodityId={Id}", commodityId);
            return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsExcludedSku(string? skuName)
    {
        if (string.IsNullOrEmpty(skuName)) return false;
        return ExcludedSkuKeywords.Any(kw =>
            skuName.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseDiscountPercent(string? discount)
    {
        if (string.IsNullOrEmpty(discount) || discount == "None") return 0;
        var idx = discount.IndexOf('%');
        if (idx <= 0) return 0;
        return int.TryParse(discount[..idx], out var pct) ? pct : 0;
    }

    // ── Response DTOs (private — không expose ra ngoài) ──────────────────────

    private sealed class SkuPageResponse
    {
        [JsonPropertyName("data")]
        public List<SkuItem>? Data { get; set; }
    }

    private sealed class SkuItem
    {
        [JsonPropertyName("discount")]
        public string? Discount { get; set; }

        [JsonPropertyName("stockStatus")]
        public int StockStatus { get; set; }

        [JsonPropertyName("promotion")]
        public int Promotion { get; set; }

        [JsonPropertyName("skuName")]
        public string? SkuName { get; set; }
    }
}
