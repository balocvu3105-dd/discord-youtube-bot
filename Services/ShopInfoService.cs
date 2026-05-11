using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

public class ShopInfoService
{
	private readonly BotConfiguration _config;
	private readonly ILogger<ShopInfoService> _logger;

	// Prefix để nhận ra button shop khi Discord gửi interaction về
	// Ví dụ: CustomId = "shop_game:Wuthering Waves"
	public const string ButtonPrefix = "shop_game:";

	public ShopInfoService(
		IOptions<BotConfiguration> config,
		ILogger<ShopInfoService> logger)
	{
		_config = config.Value;
		_logger = logger;
	}

	// ── Tạo tin nhắn tổng quan cho #thong-tin-shop ────────────────────────

	public (Embed embed, MessageComponent components) BuildShopOverview()
	{
		var games = _config.ShopGames;

		if (games.Count == 0)
			throw new InvalidOperationException("ShopGames config trống!");

		// Tạo nội dung embed tổng quan
		var embedBuilder = new EmbedBuilder()
			.WithTitle("🛒 LDShop — Nạp Game Giá Tốt Nhất!")
			.WithColor(new Color(255, 165, 0)) // màu cam
			.WithDescription(
				"**Tiết kiệm khi nạp game yêu thích qua LDShop!**\n" +
				"Bấm vào game bên dưới để xem ưu đãi riêng của bạn 👇\n" +
				"*(Chỉ bạn thấy — không ai khác!)*")
			.WithFooter($"🔄 Cập nhật mỗi {_config.ShopInfoRefreshHours}h • LDShop x Bot")
			.WithCurrentTimestamp();

		// Thêm field tóm tắt cho từng game
		foreach (var game in games)
		{
			string expireText = GetExpireText(game.ExpiresAt);
			string discountText = game.DiscountPercent > 0
				? $"**{game.DiscountPercent}% OFF**"
				: "Xem ưu đãi";

			embedBuilder.AddField(
				$"{game.Emoji} {game.Name}",
				$"{discountText} {expireText}",
				inline: true // inline: true = hiển thị 2-3 field cùng hàng
			);
		}

		// Tạo buttons — mỗi game 1 button
		// ComponentBuilder có thể chứa tối đa 5 ActionRow, mỗi row 5 buttons
		var componentBuilder = new ComponentBuilder();

		foreach (var game in games)
		{
			componentBuilder.WithButton(
				label: $"{game.Emoji} {game.Name}",
				// CustomId = "shop_game:Wuthering Waves"
				// Bot dùng cái này để biết user bấm game nào
				customId: $"{ButtonPrefix}{game.Name}",
				style: ButtonStyle.Secondary // màu xám — không link ra ngoài
			);
		}

		return (embedBuilder.Build(), componentBuilder.Build());
	}

	// ── Tạo ephemeral embed khi user bấm vào 1 game cụ thể ───────────────

	public Embed? BuildGameDetail(string gameName)
	{
		// Tìm game trong config (case-insensitive)
		var game = _config.ShopGames
			.FirstOrDefault(g =>
				g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

		if (game == null)
		{
			_logger.LogWarning("ShopInfoService: game không tìm thấy: {Name}", gameName);
			return null;
		}

		string expireText = GetExpireText(game.ExpiresAt);
		string discountLine = game.DiscountPercent > 0
			? $"💸 **Giảm {game.DiscountPercent}%** so với nạp trực tiếp!"
			: "💸 Xem giá ưu đãi tại LDShop";

		var embed = new EmbedBuilder()
			.WithTitle($"{game.Emoji} {game.Name} — Ưu Đãi Hiện Tại")
			.WithColor(new Color(0, 200, 100)) // xanh lá
			.WithDescription(
				$"{discountLine}\n" +
				(string.IsNullOrEmpty(game.PromoNote) ? "" : $"📌 {game.PromoNote}\n") +
				expireText + "\n\n" +
				$"**📖 Cách nạp:**\n{game.HowToTopUp}")
			.WithFooter("👁️ Chỉ bạn thấy tin nhắn này • LDShop")
			.WithCurrentTimestamp()
			.Build();

		return embed;
	}

	// ── Tạo button "Nạp ngay" link ra LDShop (dùng trong ephemeral) ───────

	public MessageComponent BuildGameDetailComponents(string gameName)
	{
		var game = _config.ShopGames
			.FirstOrDefault(g =>
				g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

		var builder = new ComponentBuilder();

		if (game != null && !string.IsNullOrEmpty(game.AffiliateLink))
		{
			builder.WithButton(
				label: $"🛒 Nạp ngay tại LDShop",
				style: ButtonStyle.Link, // Link button không có customId, mở URL
				url: game.AffiliateLink
			);
		}

		return builder.Build();
	}

	// ── Helper: tính thời hạn còn lại ─────────────────────────────────────

	private static string GetExpireText(string? expiresAt)
	{
		if (string.IsNullOrEmpty(expiresAt))
			return string.Empty;

		if (!DateTime.TryParse(expiresAt, out var expireDate))
			return string.Empty;

		var remaining = expireDate - DateTime.UtcNow;

		if (remaining.TotalSeconds <= 0)
			return "⚠️ Ưu đãi đã hết hạn";

		if (remaining.TotalHours < 1)
			return $"⏳ Còn **{(int)remaining.TotalMinutes} phút**!";

		if (remaining.TotalDays < 1)
			return $"⏳ Còn **{(int)remaining.TotalHours} giờ**!";

		return $"⏳ Còn **{(int)remaining.TotalDays} ngày**";
	}
}