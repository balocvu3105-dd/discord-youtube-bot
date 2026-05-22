using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Config;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    // =====================================================
    // DISCORD
    // =====================================================

    public string DiscordToken { get; set; } =
        string.Empty;

    // =====================================================
    // CHANNELS
    // =====================================================

    public ulong LiveChannelId { get; set; }

    public ulong VideoChannelId { get; set; }

    public ulong ShopChannelId { get; set; }

    // =====================================================
    // ROLES
    // =====================================================

    public ulong LiveRoleId { get; set; }

    public ulong VideoRoleId { get; set; }

    // =====================================================
    // YOUTUBE
    // =====================================================

    public string YoutubeApiKey { get; set; } =
        string.Empty;

    public string YoutubeChannelId { get; set; } =
        string.Empty;

    public int CheckIntervalSeconds { get; set; } =
        120;

    public string StateFilePath { get; set; } =
        "data/last_video_state.json";

    // =====================================================
    // SHOP
    // =====================================================

    public int ShopRefreshHours { get; set; } =
        24;

    public string ShopNotice { get; set; } =
        string.Empty;

    public List<ShopGameConfig> ShopGames { get; set; } =
        new();
}