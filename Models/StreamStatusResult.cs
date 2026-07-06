namespace YouTubeDiscordBot.Models;

/// <summary>
/// Chuẩn hóa kết quả kiểm tra trạng thái livestream từ bất kỳ nền tảng nào
/// (YouTube, TikTok, Twitch, Kick, Facebook...).
/// </summary>
public class StreamStatusResult
{
    public bool IsLive { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string UsernameOrId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public DateTime? StartedAtUtc { get; set; }

    public static StreamStatusResult Offline(string platform, string usernameOrId) => new()
    {
        IsLive = false,
        Platform = platform,
        UsernameOrId = usernameOrId
    };
}
