using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>Discount data từ một nhà cung cấp cụ thể.</summary>
public record ProviderDiscount(string ProviderName, int Percent, string AffiliateLink);

/// <summary>
/// Interface mỗi shop provider phải implement.
/// Để add shop mới: chỉ cần implement interface này, đăng ký DI → tự động hiển thị.
/// </summary>
public interface IShopDiscountProvider
{
    string Name { get; }

    /// <summary>Warm in-memory cache trước refresh cycle.</summary>
    Task WarmAsync(IEnumerable<ShopGameConfig> games, CancellationToken ct = default);

    /// <summary>% giảm giá đã cache, hoặc null nếu không có.</summary>
    int? GetDiscount(ShopGameConfig game);

    /// <summary>Affiliate link cho game, null = provider không hỗ trợ game này → bỏ qua.</summary>
    string? GetAffiliateLink(ShopGameConfig game);
}
