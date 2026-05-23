namespace YouTubeDiscordBot.Models;

public class BotState
{
	public string LastVideoId { get; set; } = string.Empty;
	public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;
}

public class VideoInfo
{
	public string VideoId { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public string ChannelName { get; set; } = string.Empty;
	public string ThumbnailUrl { get; set; } = string.Empty;
	public string LiveBroadcastContent { get; set; } = "none";
	public bool IsLive =>
		LiveBroadcastContent == "live" || LiveBroadcastContent == "upcoming";
}

public class ShopGameConfig
{
	public string Name { get; set; } = string.Empty;
	public string Emoji { get; set; } = "🎮";
	public string AffiliateLink { get; set; } = string.Empty;

	/// <summary>
	/// % giảm giá fallback — dùng khi LDShop API không trả được giá trị.
	/// Vẫn để trong config để bot không hiện "0%" khi API down.
	/// </summary>
	public int DiscountPercent { get; set; }

	public string PromoNote { get; set; } = string.Empty;
	public string TopUpType { get; set; } = string.Empty;
	public string Warning { get; set; } = string.Empty;

	/// <summary>
	/// Slug dùng để gọi LDShop API, ví dụ "wuthering-waves-gp".
	/// Để trống → bỏ qua auto-fetch, dùng DiscountPercent từ config.
	/// </summary>
	public string CommoditySeo { get; set; } = string.Empty;
}

public class ShopMessageState
{
	public ulong PinnedMessageId { get; set; }
	public Dictionary<string, ulong> GameMessageIds { get; set; } = new();
}

public class LdShopPromo
{
	public string Name { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public int DiscountPercent { get; set; }
	public string Category { get; set; } = string.Empty;

	public override bool Equals(object? obj) =>
		obj is LdShopPromo other &&
		Name == other.Name &&
		DiscountPercent == other.DiscountPercent;

	public override int GetHashCode() =>
		HashCode.Combine(Name, DiscountPercent);
}