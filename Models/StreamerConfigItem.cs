namespace YouTubeDiscordBot.Models;

/// <summary>
/// Cấu hình một kênh streamer cần theo dõi, hỗ trợ đa nền tảng.
/// </summary>
public class StreamerConfigItem
{
    /// <summary>Nền tảng (e.g. "YouTube", "TikTok", "Twitch", "Kick", "Facebook").</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>ID kênh hoặc username (e.g. "UCHfFNHHKK6phqWfordByyEQ", "catawuwa").</summary>
    public string UsernameOrId { get; set; } = string.Empty;

    /// <summary>Tên hiển thị tùy chỉnh (e.g. "CataWuwa").</summary>
    public string CustomName { get; set; } = string.Empty;

    /// <summary>Discord Channel ID để gửi thông báo (0 = dùng channel mặc định trong config).</summary>
    public ulong TargetChannelId { get; set; }

    /// <summary>Discord Role ID để mention (0 = dùng role mặc định trong config).</summary>
    public ulong TargetRoleId { get; set; }

    /// <summary>Khóa duy nhất nhận diện streamer: Platform + "_" + UsernameOrId (lowercase).</summary>
    public string Key => $"{Platform}_{UsernameOrId}".ToLowerInvariant();
}
