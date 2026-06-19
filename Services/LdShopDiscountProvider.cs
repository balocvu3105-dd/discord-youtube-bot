using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>Bọc LdShopDiscountService thành IShopDiscountProvider.</summary>
public class LdShopDiscountProvider : IShopDiscountProvider
{
    private readonly LdShopDiscountService _service;

    public string Name => "LDShop";

    public LdShopDiscountProvider(LdShopDiscountService service)
        => _service = service;

    public async Task WarmAsync(IEnumerable<ShopGameConfig> games, CancellationToken ct = default)
    {
        var pairs = games
            .Where(g => g.CommodityId > 0 && g.SkuLabelId > 0)
            .Select(g => (g.CommodityId, g.SkuLabelId));
        await _service.WarmCacheAsync(pairs, ct);
    }

    public int? GetDiscount(ShopGameConfig game)
    {
        if (game.CommodityId <= 0) return null;

        // Ưu tiên config (cập nhật thủ công từ website — chính xác nhất).
        // API LDShop trả về max SKU discount, không khớp với giá hiển thị trên trang.
        if (game.DiscountPercent > 0) return game.DiscountPercent;

        // Fallback: dùng API khi config chưa set (DiscountPercent = 0)
        var apiPct = _service.GetDiscount(game.CommodityId);
        return apiPct.HasValue ? apiPct.Value : null;
    }

    public string? GetAffiliateLink(ShopGameConfig game)
        => string.IsNullOrWhiteSpace(game.AffiliateLink) ? null : game.AffiliateLink;
}
