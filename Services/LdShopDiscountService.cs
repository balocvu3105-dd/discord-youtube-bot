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

    // FIX BUG #2: Nhận IHttpClientFactory thay vì HttpClient trực tiếp.
    // AddHttpClient<T> mặc định là Transient — nếu dùng ActivatorUtilities.CreateInstance
    // để override thành Singleton thì HttpClient được tạo ngoài IHttpClientFactory,
    // không có lifecycle management → SocketException sau vài giờ (DNS rotation bug).
    // Với IHttpClientFactory, CreateClient() luôn trả về HttpClient được quản lý đúng cách.
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
            if (pct.HasValue)
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

            // Log raw response ở Debug level để dễ debug nếu cần
            _logger.LogDebug("sku/page raw (commodityId={Id}): {Body}", commodityId, body);

            var result = JsonSerializer.Deserialize<SkuPageResponse>(body);

            // FIX BUG #1: API trả về data.records (paged object), không phải data trực tiếp là array.
            // Trước: result?.Data (List<SkuItem>) → luôn null vì data là object, không phải array
            // → discounts.Count == 0 → return 0 → pct = 0 → hiển thị "Ưu đãi" thay vì % thực tế.
            if (result?.Data?.Records is null || result.Data.Records.Count == 0)
            {
                _logger.LogWarning("Empty records — commodityId={Id}", commodityId);
                return null;
            }

            var discounts = result.Data.Records
                .Where(x => x.StockStatus == 1
                         && !string.IsNullOrEmpty(x.Discount)
                         && x.Discount != "None"
                         && !IsExcludedSku(x.SkuName))
                .Select(x => ParseDiscountPercent(x.Discount))
                .Where(x => x > 0)
                .ToList();

            if (discounts.Count == 0)
            {
                _logger.LogInformation("No discount found — commodityId={Id}", commodityId);
                return 0;
            }

            var maxDiscount = discounts.Max();

            _logger.LogInformation(
                "Discount fetched — commodityId={Id}, max={Pct}%",
                commodityId, maxDiscount);

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
        var idx = discount.IndexOf('%');
        if (idx <= 0) return 0;
        return int.TryParse(discount[..idx], out var pct) ? pct : 0;
    }

    // FIX BUG #1: Đúng cấu trúc JSON của API v4/sku/page:
    // {
    //   "data": {
    //     "records": [...],   ← array SKU ở đây
    //     "total": 100,
    //     ...
    //   }
    // }
    private class SkuPageResponse
    {
        [JsonPropertyName("data")]
        public SkuPageData? Data { get; set; }
    }

    private class SkuPageData
    {
        [JsonPropertyName("records")]
        public List<SkuItem>? Records { get; set; }
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