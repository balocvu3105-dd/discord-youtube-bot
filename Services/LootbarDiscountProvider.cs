using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>Bọc LootbarDiscountService thành IShopDiscountProvider.</summary>
public class LootbarDiscountProvider : IShopDiscountProvider
{
    private readonly LootbarDiscountService _service;
    private readonly BotConfiguration _config;

    public string Name => "Lootbar";

    public LootbarDiscountProvider(LootbarDiscountService service, IOptions<BotConfiguration> config)
    {
        _service = service;
        _config = config.Value;
    }

    public async Task WarmAsync(IEnumerable<ShopGameConfig> games, CancellationToken ct = default)
        => await _service.WarmCacheAsync(_config.LootbarShopCode);

    public int? GetDiscount(ShopGameConfig game)
    {
        if (string.IsNullOrWhiteSpace(game.LootbarGameSeo)) return null;

        if (game.LootbarAppServiceId > 0)
        {
            var apiPct = _service.GetDiscount(game.LootbarAppServiceId);
            if (apiPct.HasValue) return apiPct.Value;
        }

        return game.LootbarFallbackDiscount > 0 ? game.LootbarFallbackDiscount : null;
    }

    public string? GetAffiliateLink(ShopGameConfig game)
    {
        if (string.IsNullOrWhiteSpace(game.LootbarGameSeo)) return null;

        // Ưu tiên per-game link, fallback về main shop link
        if (!string.IsNullOrWhiteSpace(game.LootbarAffiliateLink))
            return game.LootbarAffiliateLink;

        return string.IsNullOrWhiteSpace(_config.LootbarShopLink)
            ? null
            : _config.LootbarShopLink;
    }
}
