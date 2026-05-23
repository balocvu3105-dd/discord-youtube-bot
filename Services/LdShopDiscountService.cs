using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Fetch % giảm giá thực tế từ LDShop API theo commoditySeo.
///
/// Strategy:
///   1. Gọi /commodity/page để lấy danh sách SKU của game
///   2. Ưu tiên SKU có hot=1 (featured) → tính discount từ sellPrice / officialPrice
///   3. Nếu không có hot SKU → dùng SKU đầu tiên
///   4. Nếu API lỗi → trả về null → ShopService fallback về DiscountPercent trong config
///
/// Cache:
///   Kết quả được cache theo TTL (mặc định = ShopRefreshHours) để không
///   gọi API liên tục mỗi khi BuildOverview/BuildGameEmbed được gọi.
/// </summary>
public class LdShopDiscountService
{
    private readonly HttpClient _http;
    private readonly ILogger<LdShopDiscountService> _logger;

    private const string PageUrlTemplate =
        "https://api.ldshop.gg/api/commodity/page" +
        "?commoditySeo={0}&language=vn&currency=VND&pageNum=1&pageSize=20";

    // Cache entry: (discountPercent, fetchedAt)
    private readonly Dictionary<string, (int Discount, DateTime FetchedAt)> _cache = new();
    private readonly TimeSpan _cacheTtl;

    public LdShopDiscountService(
        HttpClient http,
        ILogger<LdShopDiscountService> logger,
        TimeSpan? cacheTtl = null)
    {
        _http = http;
        _logger = logger;
        _cacheTtl = cacheTtl ?? TimeSpan.FromHours(24);

        if (!_http.DefaultRequestHeaders.Contains("Channel"))
            _http.DefaultRequestHeaders.Add("Channel", "ldshop");
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy % giảm giá cho một game theo commoditySeo.
    /// Trả về null nếu API lỗi hoặc không tính được discount.
    /// </summary>
    public async Task<int?> GetDiscountAsync(string commoditySeo)
    {
        if (string.IsNullOrWhiteSpace(commoditySeo))
            return null;

        // Trả về cache nếu còn hạn
        if (_cache.TryGetValue(commoditySeo, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < _cacheTtl)
        {
            _logger.LogDebug("[{Seo}] discount from cache: {Pct}%", commoditySeo, cached.Discount);
            return cached.Discount;
        }

        var discount = await FetchDiscountAsync(commoditySeo);

        if (discount.HasValue)
            _cache[commoditySeo] = (discount.Value, DateTime.UtcNow);

        return discount;
    }

    /// <summary>
    /// Fetch discount cho nhiều game cùng lúc (tuần tự, có delay chống rate-limit).
    /// Dùng khi ShopBackgroundService refresh — gọi 1 lần trước khi build embed.
    /// </summary>
    public async Task WarmCacheAsync(IEnumerable<string> commoditySeoList)
    {
        foreach (var seo in commoditySeoList)
        {
            if (string.IsNullOrWhiteSpace(seo)) continue;

            // Bỏ qua nếu cache còn hạn
            if (_cache.TryGetValue(seo, out var cached) &&
                DateTime.UtcNow - cached.FetchedAt < _cacheTtl)
                continue;

            var discount = await FetchDiscountAsync(seo);
            if (discount.HasValue)
                _cache[seo] = (discount.Value, DateTime.UtcNow);

            await Task.Delay(600); // 600ms giữa các request — tránh rate-limit
        }
    }

    // ── Core Fetch ───────────────────────────────────────────────────────────

    private async Task<int?> FetchDiscountAsync(string commoditySeo)
    {
        try
        {
            var url = string.Format(PageUrlTemplate, Uri.EscapeDataString(commoditySeo));
            var json = await GetJsonAsync(url);
            if (json is null) return null;

            var root = JsonSerializer.Deserialize<JsonElement>(json);

            // Kiểm tra response code
            if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200)
            {
                _logger.LogWarning("[{Seo}] API trả về code={Code}", commoditySeo, codeEl.GetInt32());
                return null;
            }

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("records", out var records) ||
                records.GetArrayLength() == 0)
            {
                _logger.LogWarning("[{Seo}] Không có records trong response", commoditySeo);
                return null;
            }

            // Ưu tiên SKU hot=1, fallback về index 0
            JsonElement? targetSku = null;
            foreach (var sku in records.EnumerateArray())
            {
                if (sku.TryGetProperty("hot", out var hot) && hot.GetInt32() == 1)
                {
                    targetSku = sku;
                    break;
                }
            }
            targetSku ??= records[0];

            var discount = CalcDiscount(targetSku.Value, commoditySeo);

            _logger.LogInformation(
                "[{Seo}] discount fetched: {Pct}%", commoditySeo, discount ?? 0);

            return discount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Seo}] FetchDiscountAsync thất bại", commoditySeo);
            return null;
        }
    }

    /// <summary>
    /// Tính % từ một SKU element.
    /// Ưu tiên field "discount" nếu có giá trị > 0.
    /// Fallback: (officialPrice - sellPrice) / officialPrice.
    /// </summary>
    private int? CalcDiscount(JsonElement sku, string seo)
    {
        // Thử field "discount" trực tiếp
        if (sku.TryGetProperty("discount", out var discEl) &&
            discEl.ValueKind != JsonValueKind.Null)
        {
            var d = discEl.GetDecimal();
            if (d > 0)
                return (int)Math.Round(d);
        }

        // Tính từ sellPriceMoney và officialPriceMoney
        if (sku.TryGetProperty("sellPriceMoney", out var sellEl) &&
            sku.TryGetProperty("officialPriceMoney", out var officialEl))
        {
            var sell = GetAmount(sellEl);
            var official = GetAmount(officialEl);

            if (official > 0 && sell >= 0 && sell < official)
                return (int)Math.Round((official - sell) / official * 100);
        }

        _logger.LogDebug("[{Seo}] Không tính được discount từ SKU", seo);
        return null;
    }

    private static decimal GetAmount(JsonElement moneyEl)
    {
        if (moneyEl.ValueKind == JsonValueKind.Null) return 0;
        if (moneyEl.TryGetProperty("amount", out var amount))
            return amount.GetDecimal();
        return 0;
    }

    // ── HTTP Helper ──────────────────────────────────────────────────────────

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