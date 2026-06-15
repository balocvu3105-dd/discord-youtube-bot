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
	/// </summary>
	public int DiscountPercent { get; set; }

	public string PromoNote { get; set; } = string.Empty;
	public string TopUpType { get; set; } = string.Empty;
	public string Warning { get; set; } = string.Empty;

	/// <summary>
	/// ID số của game trên LDShop API mới (POST /api/commodity/v4/sku/page).
	/// WuWa=10016, Genshin=10014, Arknights Endfield=10233, NTE=10165
	/// </summary>
	public int CommodityId { get; set; }

	/// <summary>
	/// Label ID đi kèm với CommodityId.
	/// WuWa=74, Genshin=82, Arknights Endfield=1, NTE=102
	/// </summary>
	public int SkuLabelId { get; set; }

	/// <summary>
	/// Slug cũ — giữ lại để không break appsettings.json cũ.
	/// Không còn dùng để gọi API nữa.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string CommoditySeo { get; set; } = string.Empty;

	// ── Lootbar ──────────────────────────────────────────────────────────
	/// <summary>
	/// Slug game trên Lootbar, e.g. "wuthering-waves".
	/// Dùng để match với API response và build link.
	/// Để trống = Lootbar disabled cho game này.
	/// </summary>
	public string LootbarGameSeo { get; set; } = string.Empty;

	/// <summary>
	/// Affiliate link riêng cho game này trên Lootbar.
	/// Nếu để trống sẽ dùng LootbarShopLink trong BotConfiguration.
	/// </summary>
	public string LootbarAffiliateLink { get; set; } = string.Empty;

	/// <summary>Fallback % giảm giá Lootbar khi API không trả được.</summary>
	public int LootbarFallbackDiscount { get; set; }

	/// <summary>
	/// app_service_id của game này trong Lootbar API.
	/// Xem logs "Lootbar cached: app_service_id=X" để xác định giá trị đúng.
	/// WuWa=226, Genshin=5, HSR=77, ZZZ=90, NTE=89, AKE=301
	/// </summary>
	public int LootbarAppServiceId { get; set; }
}

public class ShopMessageState
{
	/// <summary>Message ID của embed section LDShop.</summary>
	public ulong LdShopMessageId { get; set; }

	/// <summary>Message ID của embed section Lootbar.</summary>
	public ulong LootbarMessageId { get; set; }

	// ── Legacy fields — giữ để không lỗi khi load file state cũ ──────────
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public ulong PinnedMessageId { get; set; }
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
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