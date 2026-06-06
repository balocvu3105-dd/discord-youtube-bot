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

    private readonly ConcurrentDictionary<int, int> _cache = new();

    private static readonly string[] ExcludedSkuKeywords =
    [
        "New User Exclusive",
        "Bundle",
        "Guarantee",
        "Collection",
        "Outfit",
        "Subscription",
        "Upgrade",
        "Chassis",
        "Ring",
        "Aid",
        "Protocol",
        "Lightpack",
        "Heavypack",
        "Voyage",
        "Prep",
        "Insider Channel",
        "Connoisseur Channel",
    ];

    // FIX BUG #2: IHttpClientFactory thay vì HttpClient trực tiếp
    public LdShopDiscountService(
        IHttpClientFactory httpClientFactory,
        ILogger<LdShopDiscountService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(LdShopDiscountService));
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

    public async Task WarmCacheAsync(IEnumerable<(int commodityId, int skuLabelId)> games)
    {
        foreach (var (commodityId, skuLabelId) in games)
        {
            var pct = await FetchDiscountAsync(commodityId, skuLabelId);
            // Chỉ cache khi pct > 0 — nếu API trả về 0 (no discount found)
            // thì KHÔNG cache, để ResolveDiscount fallback về DiscountPercent trong appsettings.
            if (pct.HasValue && pct.Value > 0)
                _cache[commodityId] = (int)Math.Round(pct.Value);
        }
    }

    public int? GetDiscount(int commodityId)
        => _cache.TryGetValue(commodityId, out var v) ? v : null;

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

            // Log 300 ký tự đầu để debug structure thực tế
            _logger.LogInformation("sku/page preview (commodityId={Id}): {Preview}",
                commodityId, body.Length > 300 ? body[..300] : body);

            var result = JsonSerializer.Deserialize<SkuPageResponse>(body);

            if (result?.Data is null || result.Data.Count == 0)
            {
                _logger.LogWarning("Empty data — commodityId={Id}", commodityId);
                return null;
            }

            _logger.LogInformation("Total SKUs before filter (commodityId={Id}): {Count}",
                commodityId, result.Data.Count);

            foreach (var sku in result.Data.Take(5))
            {
                _logger.LogInformation(
                    "  SKU sample — name={Name}, discount={Discount}, stockStatus={Stock}, excluded={Excl}",
                    sku.SkuName, sku.Discount, sku.StockStatus, IsExcludedSku(sku.SkuName));
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
                _logger.LogInformation("No discount found after filter — commodityId={Id}", commodityId);
                return 0;
            }

            var maxDiscount = discounts.Max();
            _logger.LogInformation("Discount fetched — commodityId={Id}, max={Pct}%", commodityId, maxDiscount);
            return maxDiscount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchDiscountAsync failed — commodityId={Id}", commodityId);
            return null;
        }
    }

    private static bool IsExcludedSku(string? skuName)
    {
        if (string.IsNullOrEmpty(skuName)) return false;
        return ExcludedSkuKeywords.Any(kw =>
            skuName.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseDiscountPercent(string? discount)
    {
        if (string.IsNullOrEmpty(discount) || discount == "None") return 0;
        // Format API trả về: "28%OFF" → lấy số trước %
        var idx = discount.IndexOf('%');
        if (idx <= 0) return 0;
        return int.TryParse(discount[..idx], out var pct) ? pct : 0;
    }

    // REVERT: data là array trực tiếp, không phải object có records
    // Confirmed từ curl: {"code":200,"msg":"success","data":[{...}]}
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

        [JsonPropertyName("skuName")]
        public string? SkuName { get; set; }
    }
}