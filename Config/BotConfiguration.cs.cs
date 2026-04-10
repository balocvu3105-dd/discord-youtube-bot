namespace YouTubeDiscordBot.Config;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    // Đổi tên để khớp với Program.cs nếu bạn dùng DiscordToken
    public string DiscordToken { get; set; } = string.Empty;

    public string ChannelName { get; set; } = "youtube-noti";

    // Sửa thành chữ 'u' thường để khớp với lỗi CS1061 trong YouTubeService.cs
    public string YoutubeApiKey { get; set; } = string.Empty;
    public string YoutubeChannelId { get; set; } = string.Empty;

    public int CheckIntervalSeconds { get; set; } = 600;
    public string StateFilePath { get; set; } = "last_video_state.json";

    // Thêm ID phòng Discord nếu bạn có dùng
    public ulong DiscordChannelId { get; set; }
}