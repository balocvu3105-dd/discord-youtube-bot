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
        await _service.WarmCacheAsync(pairs);
    }

    public int? GetDiscount(ShopGameConfig game)
    {
        if (game.CommodityId <= 0) return null;

        // Ưu tiên API, fallback về config
        var apiPct = _service.GetDiscount(game.CommodityId);
        if (apiPct.HasValue) return apiPct.Value;
        return game.DiscountPercent > 0 ? game.DiscountPercent : null;
    }

    public string? GetAffiliateLink(ShopGameConfig game)
        => string.IsNullOrWhiteSpace(game.AffiliateLink) ? null : game.AffiliateLink;
}
