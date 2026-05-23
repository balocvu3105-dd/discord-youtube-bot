using System.ComponentModel.DataAnnotations;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Config;

/// <summary>
/// Toàn bộ config của bot, bind từ appsettings.json section "BotConfiguration".
/// [Required] giúp bot fail fast ngay lúc startup thay vì crash muộn hơn.
/// </summary>
public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    // ── Discord ──────────────────────────────────────────────────────────
    [Required(ErrorMessage = "DiscordToken không được để trống")]
    public string DiscordToken { get; set; } = string.Empty;

    // ── Channels ─────────────────────────────────────────────────────────
    [Range(1, ulong.MaxValue, ErrorMessage = "LiveChannelId phải là ID hợp lệ")]
    public ulong LiveChannelId { get; set; }

    [Range(1, ulong.MaxValue, ErrorMessage = "VideoChannelId phải là ID hợp lệ")]
    public ulong VideoChannelId { get; set; }

    [Range(1, ulong.MaxValue, ErrorMessage = "ShopChannelId phải là ID hợp lệ")]
    public ulong ShopChannelId { get; set; }

    // ── Roles ────────────────────────────────────────────────────────────
    public ulong LiveRoleId { get; set; }
    public ulong VideoRoleId { get; set; }

    // ── YouTube ──────────────────────────────────────────────────────────
    [Required(ErrorMessage = "YoutubeApiKey không được để trống")]
    public string YoutubeApiKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "YoutubeChannelId không được để trống")]
    public string YoutubeChannelId { get; set; } = string.Empty;

    /// <summary>Khoảng thời gian poll YouTube API, tính bằng giây. Default 120s.</summary>
    [Range(30, 3600, ErrorMessage = "CheckIntervalSeconds phải từ 30 đến 3600")]
    public int CheckIntervalSeconds { get; set; } = 120;

    // ── Persistence ──────────────────────────────────────────────────────
    /// <summary>
    /// Đường dẫn file lưu last video ID.
    /// QUAN TRỌNG: Phải trỏ vào thư mục được mount vào Docker volume
    /// để data không bị mất khi container restart.
    /// </summary>
    public string StateFilePath { get; set; } = "data/last_video_state.json";

    /// <summary>Đường dẫn file lưu live state cache.</summary>
    public string LiveStateFilePath { get; set; } = "data/live_state.json";

    // ── Shop ─────────────────────────────────────────────────────────────
    [Range(1, 168, ErrorMessage = "ShopRefreshHours phải từ 1 đến 168")]
    public int ShopRefreshHours { get; set; } = 24;

    public string ShopNotice { get; set; } = string.Empty;

    public List<ShopGameConfig> ShopGames { get; set; } = new();
}