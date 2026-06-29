using System.Text.Json;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Gọi API công khai của LDShop để lấy % giảm giá thực tế.
/// Chưa được tích hợp vào bot (future feature) — không đăng ký trong DI.
/// </summary>
public class LdShopScraperService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LdShopScraperService> _logger;

    private const string HttpClientName = nameof(LdShopDiscountService);

    private const string AllCommodityUrl =
        "https://api.ldshop.gg/api/commodity/allCommodity";
    private const string PriceUrlTemplate =
        "https://api.ldshop.gg/api/commodity/v2/calculate/price?currency=VND&skuId={0}&num=1";
    private const string PageUrlTemplate =
        "https://api.ldshop.gg/api/commodity/page?commoditySeo={0}&language=vn&currency=VND&pageNum=1&pageSize=1";

    // Thread-safe lazy cache: chỉ fetch allCommodity 1 lần, dù nhiều coroutine gọi đồng thời.
    // SemaphoreSlim bảo vệ phần populate.
    private readonly SemaphoreSlim _nameCacheLock = new(1, 1);
    private volatile Dictionary<string, string>? _nameCache;

    public LdShopScraperService(
        IHttpClientFactory httpClientFactory,
        ILogger<LdShopScraperService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<LdShopPromo>> FetchPromosAsync(
        IEnumerable<string> commoditySeoList,
        CancellationToken ct = default)
    {
        var result = new List<LdShopPromo>();

        foreach (var seo in commoditySeoList)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var promo = await FetchOnePromoAsync(seo, ct);
                if (promo is not null)
                {
                    result.Add(promo);
                    _logger.LogInformation("{Seo} → {Discount}%", seo, promo.DiscountPercent);
                }
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch promo for {Seo}", seo);
            }
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<LdShopPromo?> FetchOnePromoAsync(string commoditySeo, CancellationToken ct)
    {
        var skuId = await GetFirstSkuIdAsync(commoditySeo, ct);
        if (skuId is null) return null;

        var discount = await GetDiscountPercentAsync(skuId.Value, ct);
        var name = await GetGameNameAsync(commoditySeo, ct);

        return new LdShopPromo
        {
            Name = name ?? commoditySeo,
            Url = $"https://www.ldshop.gg/vn/top-up/{commoditySeo}.html",
            DiscountPercent = discount,
            Category = "top-up"
        };
    }

    private async Task<int?> GetFirstSkuIdAsync(string commoditySeo, CancellationToken ct)
    {
        var url = string.Format(PageUrlTemplate, commoditySeo);
        var json = await GetJsonAsync(url, ct);
        if (json is null) return null;

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

    private async Task<int> GetDiscountPercentAsync(int skuId, CancellationToken ct)
    {
        var url = string.Format(PriceUrlTemplate, skuId);
        var json = await GetJsonAsync(url, ct);
        if (json is null) return 0;

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

    /// <summary>
    /// Lazy load tên game — thread-safe qua SemaphoreSlim.
    /// Nếu nhiều coroutine cùng gọi lần đầu, chỉ 1 coroutine fetch HTTP,
    /// các coroutine còn lại chờ và dùng kết quả đã cache.
    /// </summary>
    private async Task<string?> GetGameNameAsync(string commoditySeo, CancellationToken ct)
    {
        // Fast path: cache đã có, không cần lock
        if (_nameCache is not null)
            return _nameCache.TryGetValue(commoditySeo, out var cached) ? cached : null;

        await _nameCacheLock.WaitAsync(ct);
        try
        {
            // Double-check sau khi vào lock
            if (_nameCache is not null)
                return _nameCache.TryGetValue(commoditySeo, out var cached) ? cached : null;

            var newCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = await GetJsonAsync(AllCommodityUrl, ct);

            if (json is not null)
            {
                try
                {
                    var root = JsonSerializer.Deserialize<JsonElement>(json);
                    foreach (var item in root.GetProperty("data").EnumerateArray())
                    {
                        var seo = item.GetProperty("commoditySeo").GetString() ?? "";
                        var name = item.GetProperty("commodityName").GetString() ?? seo;
                        if (!string.IsNullOrEmpty(seo))
                            newCache[seo] = name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Parse allCommodity failed");
                }
            }

            // Assign cuối cùng — volatile write qua assignment đủ để fast-path đọc đúng
            _nameCache = newCache;
            return _nameCache.TryGetValue(commoditySeo, out var result) ? result : null;
        }
        finally
        {
            _nameCacheLock.Release();
        }
    }

    private async Task<string?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP {Status} from {Url}", (int)response.StatusCode, url);
                return null;
            }
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET failed: {Url}", url);
            return null;
        }
    }
}
