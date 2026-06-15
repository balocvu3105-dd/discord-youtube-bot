using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeDiscordBot.Services;

public class LdShopDiscountService
{
    // Lưu factory thay vì HttpClient instance.
    // Mỗi lần FetchDiscountAsync được gọi sẽ tạo HttpClient mới từ factory,
    // đảm bảo HttpMessageHandler được rotate đúng chu kỳ (2 phút mặc định)
    // → tránh DNS staleness khi bot chạy liên tục nhiều ngày.
    private readonly IHttpClientFactory _httpClientFactory;
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

    public LdShopDiscountService(
        IHttpClientFactory httpClientFactory,
        ILogger<LdShopDiscountService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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

            // Tạo client mới mỗi request — factory quản lý handler pool,
            // HttpClient instance được dispose sau khi dùng xong.
            using var httpClient = _httpClientFactory.CreateClient(nameof(LdShopDiscountService));
            var response = await httpClient.PostAsync(SkuPageUrl, content);

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