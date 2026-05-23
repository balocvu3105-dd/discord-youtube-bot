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
    /// Fetch discount từ API.
    ///
    /// Cách hoạt động của LDShop API:
    ///   - promotion=0 → SKU thường, không giảm giá → discount = "None"
    ///   - promotion=1 → Limited-Time Offer → có discount thật (vd: "21%OFF")
    ///   - promotion=2 → New User Discount → chỉ dành cho user mới, không tính
    ///
    /// Web LDShop hiển thị badge = max discount của promotion=1.
    /// → Bot lấy đúng promotion=1, parse field "discount" = "21%OFF", lấy max.
    /// </summary>
    public async Task<double?> FetchDiscountAsync(int commodityId, int skuLabelId)
    {
        try
        {
            // Giữ skuLabelId — bắt buộc có nếu không API trả data: []
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
            {
                _logger.LogWarning("Empty data — commodityId={Id}", commodityId);
                return null;
            }

            // Chỉ lấy Limited-Time Offer (promotion=1) đang còn hàng (stockStatus=1)
            // Đây là loại discount thật hiển thị trên web LDShop
            var limitedItems = result.Data
                .Where(x => x.Promotion == 1
                         && x.StockStatus == 1
                         && !string.IsNullOrEmpty(x.Discount)
                         && x.Discount != "None")
                .ToList();

            if (limitedItems.Count == 0)
            {
                // Không có limited-time offer → game không có promo hiện tại
                _logger.LogInformation(
                    "No limited-time discount — commodityId={Id}", commodityId);
                return 0;
            }

            // Parse "21%OFF" → 21
            var discounts = limitedItems
                .Select(x => ParseDiscountPercent(x.Discount))
                .Where(x => x > 0)
                .ToList();

            if (discounts.Count == 0) return 0;

            // Lấy max — đúng với badge web LDShop hiển thị
            var maxDiscount = discounts.Max();

            _logger.LogInformation(
                "Discount fetched — commodityId={Id}, limited={Count}, max={Pct}%",
                commodityId, limitedItems.Count, maxDiscount);

            return maxDiscount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchDiscountAsync failed — commodityId={Id}", commodityId);
            return null;
        }
    }

    /// <summary>
    /// Parse "21%OFF" → 21, "None" → 0, null → 0
    /// </summary>
    private static int ParseDiscountPercent(string? discount)
    {
        if (string.IsNullOrEmpty(discount) || discount == "None") return 0;

        // Format: "21%OFF" → tách số trước dấu %
        var idx = discount.IndexOf('%');
        if (idx <= 0) return 0;

        return int.TryParse(discount[..idx], out var pct) ? pct : 0;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────

    private class SkuPageResponse
    {
        [JsonPropertyName("data")]
        public List<SkuItem>? Data { get; set; }
    }

    private class SkuItem
    {
        [JsonPropertyName("discount")]
        public string? Discount { get; set; }

        [JsonPropertyName("stockStatus")]
        public int StockStatus { get; set; }

        [JsonPropertyName("promotion")]
        public int Promotion { get; set; }
    }
}