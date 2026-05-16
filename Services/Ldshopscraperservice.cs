using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Gọi API công khai của LDShop để lấy % giảm giá thực tế.
/// Không cần đăng nhập — chỉ cần header "Channel: ldshop".
/// </summary>
public class LdShopScraperService
{
    private readonly HttpClient _http;
    private readonly ILogger<LdShopScraperService> _logger;

    // ----------------------------------------------------------------
    // API 1: Lấy danh sách tất cả game
    // Trả về: commodityId, commodityName, commoditySeo
    // ----------------------------------------------------------------
    private const string AllCommodityUrl =
        "https://api.ldshop.gg/api/commodity/allCommodity";

    // ----------------------------------------------------------------
    // API 2: Tính giá thực tế của 1 SKU
    // skuId  = ID của gói nạp cụ thể (lấy từ API page)
    // Trả về: orderMoney (giá sale), officialMoney (giá gốc)
    // ----------------------------------------------------------------
    private const string PriceUrlTemplate =
        "https://api.ldshop.gg/api/commodity/v2/calculate/price?currency=VND&skuId={0}&num=1";

    // ----------------------------------------------------------------
    // API 3: Lấy skuId đầu tiên của 1 game theo commoditySeo
    // ----------------------------------------------------------------
    private const string PageUrlTemplate =
        "https://api.ldshop.gg/api/commodity/page?commoditySeo={0}&language=vn&currency=VND&pageNum=1&pageSize=1";

    public LdShopScraperService(
        HttpClient http,
        ILogger<LdShopScraperService> logger)
    {
        _http = http;
        _logger = logger;

        // Header bắt buộc — không có sẽ bị từ chối
        if (!_http.DefaultRequestHeaders.Contains("Channel"))
            _http.DefaultRequestHeaders.Add("Channel", "ldshop");
    }

    // ================================================================
    // PUBLIC: Lấy danh sách game kèm % giảm giá thực tế
    // ================================================================

    public async Task<List<LdShopPromo>> FetchPromosAsync(
        IEnumerable<string> commoditySeoList)
    {
        var result = new List<LdShopPromo>();

        foreach (var seo in commoditySeoList)
        {
            try
            {
                var promo = await FetchOnePromoAsync(seo);
                if (promo != null)
                {
                    result.Add(promo);
                    _logger.LogInformation(
                        "✅ {Seo} → {Discount}%", seo, promo.DiscountPercent);
                }

                // Delay nhỏ để tránh spam API
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to fetch promo for {Seo}", seo);
            }
        }

        return result;
    }

    // ================================================================
    // PRIVATE: Lấy promo cho 1 game
    // ================================================================

    private async Task<LdShopPromo?> FetchOnePromoAsync(string commoditySeo)
    {
        // Bước 1: Lấy skuId đầu tiên của game này
        var skuId = await GetFirstSkuIdAsync(commoditySeo);
        if (skuId == null)
        {
            _logger.LogWarning("⚠️ Không tìm được skuId cho {Seo}", commoditySeo);
            return null;
        }

        // Bước 2: Tính % giảm giá từ skuId đó
        var discount = await GetDiscountPercentAsync(skuId.Value);

        // Bước 3: Lấy tên game
        var name = await GetGameNameAsync(commoditySeo);

        return new LdShopPromo
        {
            Name = name ?? commoditySeo,
            Url = $"https://www.ldshop.gg/vn/top-up/{commoditySeo}.html",
            DiscountPercent = discount,
            Category = "top-up"
        };
    }

    // ================================================================
    // Lấy skuId đầu tiên từ trang game
    // ================================================================

    private async Task<int?> GetFirstSkuIdAsync(string commoditySeo)
    {
        var url = string.Format(PageUrlTemplate, commoditySeo);

        var json = await GetJsonAsync(url);
        if (json == null) return null;

        // Cấu trúc: { data: { records: [ { skuId: 123, ... } ] } }
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var records = root
                .GetProperty("data")
                .GetProperty("records");

            if (records.GetArrayLength() == 0) return null;

            // Lấy skuId của gói đầu tiên (thường là gói rẻ nhất / phổ biến nhất)
            return records[0].GetProperty("skuId").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parse skuId failed for {Seo}", commoditySeo);
            return null;
        }
    }

    // ================================================================
    // Tính % giảm giá từ skuId
    // ================================================================

    private async Task<int> GetDiscountPercentAsync(int skuId)
    {
        var url = string.Format(PriceUrlTemplate, skuId);

        var json = await GetJsonAsync(url);
        if (json == null) return 0;

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var data = root.GetProperty("data");

            // Giá gốc và giá sale
            var official = data
                .GetProperty("officialMoney")
                .GetProperty("amount")
                .GetDecimal();

            var order = data
                .GetProperty("orderMoney")
                .GetProperty("amount")
                .GetDecimal();

            if (official <= 0) return 0;

            // % giảm = (giá gốc - giá sale) / giá gốc * 100
            var discountPercent = (int)Math.Round((official - order) / official * 100);
            return Math.Max(0, discountPercent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parse discount failed for skuId {SkuId}", skuId);
            return 0;
        }
    }

    // ================================================================
    // Lấy tên game từ API allCommodity (cache đơn giản)
    // ================================================================

    private Dictionary<string, string>? _nameCache;

    private async Task<string?> GetGameNameAsync(string commoditySeo)
    {
        // Nạp cache 1 lần
        if (_nameCache == null)
        {
            _nameCache = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            var json = await GetJsonAsync(AllCommodityUrl);
            if (json != null)
            {
                try
                {
                    var root = JsonSerializer.Deserialize<JsonElement>(json);
                    var items = root.GetProperty("data");

                    foreach (var item in items.EnumerateArray())
                    {
                        var seo = item.GetProperty("commoditySeo")
                            .GetString() ?? "";
                        var name = item.GetProperty("commodityName")
                            .GetString() ?? seo;

                        if (!string.IsNullOrEmpty(seo))
                            _nameCache[seo] = name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Parse allCommodity failed");
                }
            }
        }

        return _nameCache.TryGetValue(commoditySeo, out var n) ? n : null;
    }

    // ================================================================
    // Helper: GET JSON string
    // ================================================================

    private async Task<string?> GetJsonAsync(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "HTTP {Status} from {Url}",
                    (int)response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET failed: {Url}", url);
            return null;
        }
    }
}