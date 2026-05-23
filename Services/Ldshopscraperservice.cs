using System.Text.Json;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Gọi API công khai của LDShop để lấy % giảm giá thực tế.
/// Hiện tại chưa được tích hợp vào bot (future feature).
/// Không cần đăng nhập — chỉ cần header "Channel: ldshop".
/// </summary>
public class LdShopScraperService
{
    private readonly HttpClient _http;
    private readonly ILogger<LdShopScraperService> _logger;

    private const string AllCommodityUrl =
        "https://api.ldshop.gg/api/commodity/allCommodity";
    private const string PriceUrlTemplate =
        "https://api.ldshop.gg/api/commodity/v2/calculate/price?currency=VND&skuId={0}&num=1";
    private const string PageUrlTemplate =
        "https://api.ldshop.gg/api/commodity/page?commoditySeo={0}&language=vn&currency=VND&pageNum=1&pageSize=1";

    public LdShopScraperService(HttpClient http, ILogger<LdShopScraperService> logger)
    {
        _http = http;
        _logger = logger;

        if (!_http.DefaultRequestHeaders.Contains("Channel"))
            _http.DefaultRequestHeaders.Add("Channel", "ldshop");
    }

    public async Task<List<LdShopPromo>> FetchPromosAsync(IEnumerable<string> commoditySeoList)
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
                    _logger.LogInformation("{Seo} → {Discount}%", seo, promo.DiscountPercent);
                }
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch promo for {Seo}", seo);
            }
        }

        return result;
    }

    private async Task<LdShopPromo?> FetchOnePromoAsync(string commoditySeo)
    {
        var skuId = await GetFirstSkuIdAsync(commoditySeo);
        if (skuId == null) return null;

        var discount = await GetDiscountPercentAsync(skuId.Value);
        var name = await GetGameNameAsync(commoditySeo);

        return new LdShopPromo
        {
            Name = name ?? commoditySeo,
            Url = $"https://www.ldshop.gg/vn/top-up/{commoditySeo}.html",
            DiscountPercent = discount,
            Category = "top-up"
        };
    }

    private async Task<int?> GetFirstSkuIdAsync(string commoditySeo)
    {
        var url = string.Format(PageUrlTemplate, commoditySeo);
        var json = await GetJsonAsync(url);
        if (json == null) return null;

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var records = root.GetProperty("data").GetProperty("records");
            if (records.GetArrayLength() == 0) return null;
            return records[0].GetProperty("skuId").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parse skuId failed for {Seo}", commoditySeo);
            return null;
        }
    }

    private async Task<int> GetDiscountPercentAsync(int skuId)
    {
        var url = string.Format(PriceUrlTemplate, skuId);
        var json = await GetJsonAsync(url);
        if (json == null) return 0;

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var data = root.GetProperty("data");
            var official = data.GetProperty("officialMoney").GetProperty("amount").GetDecimal();
            var order = data.GetProperty("orderMoney").GetProperty("amount").GetDecimal();
            if (official <= 0) return 0;
            return Math.Max(0, (int)Math.Round((official - order) / official * 100));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parse discount failed for skuId {SkuId}", skuId);
            return 0;
        }
    }

    private Dictionary<string, string>? _nameCache;

    private async Task<string?> GetGameNameAsync(string commoditySeo)
    {
        if (_nameCache == null)
        {
            _nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = await GetJsonAsync(AllCommodityUrl);
            if (json != null)
            {
                try
                {
                    var root = JsonSerializer.Deserialize<JsonElement>(json);
                    foreach (var item in root.GetProperty("data").EnumerateArray())
                    {
                        var seo = item.GetProperty("commoditySeo").GetString() ?? "";
                        var name = item.GetProperty("commodityName").GetString() ?? seo;
                        if (!string.IsNullOrEmpty(seo)) _nameCache[seo] = name;
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Parse allCommodity failed"); }
            }
        }
        return _nameCache.TryGetValue(commoditySeo, out var n) ? n : null;
    }

    private async Task<string?> GetJsonAsync(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP {Status} from {Url}", (int)response.StatusCode, url);
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