using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Thu thập discount từ tất cả IShopDiscountProvider và so sánh.
/// Để add shop mới: implement IShopDiscountProvider + đăng ký DI — không cần sửa class này.
/// </summary>
public class ShopDiscountAggregator
{
    private readonly IEnumerable<IShopDiscountProvider> _providers;
    private readonly ILogger<ShopDiscountAggregator> _logger;

    public ShopDiscountAggregator(
        IEnumerable<IShopDiscountProvider> providers,
        ILogger<ShopDiscountAggregator> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    /// <summary>Warm tất cả providers song song.</summary>
    public async Task WarmAllAsync(IEnumerable<ShopGameConfig> games, CancellationToken ct = default)
    {
        var gameList = games.ToList();
        var tasks = _providers.Select(async provider =>
        {
            try { await provider.WarmAsync(gameList, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Provider}] WarmAsync thất bại", provider.Name);
            }
        });
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Trả về list discount từ các provider hỗ trợ game này,
    /// sort theo % giảm giá giảm dần (bên rẻ nhất lên đầu).
    /// </summary>
    public List<ProviderDiscount> GetDiscounts(ShopGameConfig game)
    {
        var result = new List<ProviderDiscount>();

        foreach (var provider in _providers)
        {
            var link = provider.GetAffiliateLink(game);
            if (link is null) continue; // provider không support game này

            var pct = provider.GetDiscount(game);
            if (pct is null) continue; // không có data

            result.Add(new ProviderDiscount(provider.Name, pct.Value, link));
        }

        return [.. result.OrderByDescending(x => x.Percent)];
    }
}
