using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeDiscordBot.Services;

public class LdShopDiscountService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LdShopDiscountService> _logger;

    private const string SkuPageUrl = "https://api.ldshop.gg/api/commodity/v4/sku/page";

    // Cache: commodityId → discount%
    private readonly ConcurrentDictionary<int, int> _cache = new();

    public LdShopDiscountService(
        HttpClient httpClient,
        ILogger<LdShopDiscountService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.ldshop.gg");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.ldshop.gg/");
        _httpClient.DefaultRequestHeaders.Add("Channel", "ldshop");
        _httpClient.DefaultRequestHeaders.Add("Currency", "VND");
        _httpClient.DefaultRequestHeaders.Add("Cversion", "v2");
        _httpClient.DefaultRequestHeaders.Add("Language", "vn");
        _httpClient.DefaultRequestHeaders.Add("Source", "pc");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-site");
    }

    /// <summary>
    /// Warm cache cho tất cả games trước khi build embeds.
    /// </summary>
    public async Task WarmCacheAsync(IEnumerable<(int commodityId, int skuLabelId)> games)
    {
        foreach (var (commodityId, skuLabelId) in games)
        {
            var pct = await FetchDiscountAsync(commodityId, skuLabelId);
            if (pct.HasValue)
                _cache[commodityId] = (int)Math.Round(pct.Value);
        }
    }

    /// <summary>
    /// Lấy discount từ cache (đã warm trước đó).
    /// </summary>
    public int? GetDiscount(int commodityId)
        => _cache.TryGetValue(commodityId, out var v) ? v : null;

    /// <summary>
    /// Fetch trực tiếp từ API (không qua cache).
    /// </summary>
    public async Task<double?> FetchDiscountAsync(int commodityId, int skuLabelId)
    {
        try
        {
            var payload = new
            {
                page = new { current = 1, size = 100 },
                commodityId,
                skuLabelId
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(SkuPageUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("HTTP {Status} from sku/page (commodityId={Id}) — body: {Body}",
                    (int)response.StatusCode, commodityId, errorBody);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SkuPageResponse>(body);

            if (result?.Data is null || result.Data.Count == 0)
                return null;

            var validItems = result.Data
                .Where(x => x.Promotion == 0
                         && x.StockStatus == 1
                         && x.SellPrice?.Amount > 0
                         && x.TotalDiscount?.Amount > 0)
                .ToList();

            if (validItems.Count == 0)
            {
                _logger.LogWarning("No valid items — commodityId={Id}", commodityId);
                return null;
            }

            var avgDiscount = validItems
                .Average(x => x.TotalDiscount!.Amount / x.SellPrice!.Amount * 100);

            _logger.LogInformation(
                "Discount fetched — commodityId={Id}, items={Count}, avg={Pct:F1}%",
                commodityId, validItems.Count, avgDiscount);

            return Math.Round(avgDiscount, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchDiscountAsync failed — commodityId={Id}", commodityId);
            return null;
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────

    private class SkuPageResponse
    {
        [JsonPropertyName("data")]
        public List<SkuItem>? Data { get; set; }
    }

    private class SkuItem
    {
        [JsonPropertyName("sellPriceMoney")]
        public MoneyInfo? SellPrice { get; set; }

        [JsonPropertyName("totalDiscountMoney")]
        public MoneyInfo? TotalDiscount { get; set; }

        [JsonPropertyName("stockStatus")]
        public int StockStatus { get; set; }

        [JsonPropertyName("promotion")]
        public int Promotion { get; set; }
    }

    private class MoneyInfo
    {
        [JsonPropertyName("amount")]
        public double Amount { get; set; }
    }
}