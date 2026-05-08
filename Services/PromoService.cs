using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class PromoService
{
    private readonly BotConfiguration _config;
    private readonly ILogger<PromoService> _logger;

    private readonly List<GamePromo> _games;

    private int _currentIndex = 0;

    // FIX: Thread-safe Random
    private static readonly ThreadLocal<Random> _random = new(() => new Random());

    public PromoService(IOptions<BotConfiguration> config, ILogger<PromoService> logger)
    {
        _config = config.Value;
        _logger = logger;

        var emojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Arknights Endfield", "⚙️" },
            { "Genshin Impact",     "🌍" },
            { "Honkai Star Rail",   "🚂" },
            { "Wuthering Waves",    "🌊" },
            { "Zenless Zone Zero",  "⚡" },
        };

        _games = _config.PromoGames.Select(g => new GamePromo
        {
            Name = g.Name,
            AffiliateLink = g.AffiliateLink,
            Emoji = emojiMap.TryGetValue(g.Name, out var emoji) ? emoji : "🎮"
        }).ToList();
    }

    // ========================= PUBLIC =========================

    public (Embed embed, MessageComponent components) BuildNextPromo()
    {
        if (_games.Count == 0)
            throw new InvalidOperationException("Không có game nào trong PromoGames config!");

        var game = _games[_currentIndex % _games.Count];
        _currentIndex++;

        _logger.LogInformation("📢 Promo [{Index}/{Total}] → {Game}",
            _currentIndex, _games.Count, game.Name);

        return BuildPromo(game);
    }

    public (Embed embed, MessageComponent components)? BuildPromoForGame(string gameName)
    {
        var game = _games.FirstOrDefault(g =>
            g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

        if (game == null)
        {
            _logger.LogWarning("⚠️ Game không tìm thấy: {Name}", gameName);
            return null;
        }

        return BuildPromo(game);
    }

    public List<string> GetGameNames() => _games.Select(g => g.Name).ToList();

    // ========================= CORE =========================

    private (Embed embed, MessageComponent components) BuildPromo(GamePromo game)
    {
        // 3 version A/B testing
        int variant = _random.Value.Next(3);

        (string title, string description, string buttonText) content = variant switch
        {
            0 => VersionHype(game),
            1 => VersionUrgent(game),
            _ => VersionSocial(game)
        };

        var embed = new EmbedBuilder()
            .WithTitle(content.title)
            .WithDescription(content.description)
            .WithColor(new Color(255, 140, 0))
            .WithFooter($"⏳ Ưu đãi có hạn • LDShop x {game.Name}")
            .WithCurrentTimestamp()
            .Build();

        var components = new ComponentBuilder()
            .WithButton(
                label: content.buttonText,
                style: ButtonStyle.Link,
                url: game.AffiliateLink
            )
            .Build();

        return (embed, components);
    }

    // ========================= CONTENT =========================

    private static (string title, string description, string buttonText) VersionHype(GamePromo game) =>
    (
        $"⏰ {game.Emoji} {game.Name} — GIẢM 15% CHỈ 48H!",
        $"💥 **DEAL KHỦNG cho game thủ!**\n\n" +
        $"{game.Emoji} Nạp **{game.Name}** tiết kiệm ngay **15%** 💸\n" +
        $"⚡ Áp dụng toàn bộ mệnh giá — không giới hạn\n" +
        $"🎮 Thanh toán nhanh, an toàn qua LDShop\n\n" +
        $"⏳ **Chỉ áp dụng trong 48h cho tài khoản mới!**\n" +
        $"⚠️ Nạp trực tiếp = giá cao hơn!\n\n" +
        $"> 👇 Click ngay trước khi hết ưu đãi!",
        "🎮 Nạp ngay -15%"
    );

    private static (string title, string description, string buttonText) VersionUrgent(GamePromo game) =>
    (
        $"🚨 {game.Emoji} {game.Name} — -15% CHỈ 48H!",
        $"🔥 **Ưu đãi giới hạn — hết là mất!**\n\n" +
        $"✅ Giảm **15%** khi nạp qua link\n" +
        $"✅ Không cần nhập mã — tự động áp dụng\n" +
        $"✅ Hàng nghìn game thủ đã dùng 👀\n\n" +
        $"⏳ Chỉ trong **48h đầu cho user mới**\n" +
        $"⚠️ Nạp trực tiếp = tốn nhiều tiền hơn\n\n" +
        $"> 👇 Bấm ngay — tiết kiệm liền tay!",
        "🔥 Nhận ưu đãi ngay"
    );

    private static (string title, string description, string buttonText) VersionSocial(GamePromo game) =>
    (
        $"👀 {game.Emoji} {game.Name} — Game Thủ Đang Nạp Ở Đây!",
        $"🔥 **10.000+ người đã nạp qua LDShop!**\n\n" +
        $"💸 Giảm ngay **15%** khi nạp {game.Name}\n" +
        $"⚡ Nhanh — an toàn — giá tốt hơn\n\n" +
        $"⏳ Chỉ áp dụng trong 48h đầu!",
        "🎮 Nạp ngay -15%"
    );
}