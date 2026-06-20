namespace YouTubeDiscordBot.Models;

public class BotState
{
    /// <summary>
    /// Legacy — chỉ dùng để migrate dữ liệu cũ.
    /// Dùng LastVideoIds thay thế.
    /// </summary>
    public string LastVideoId { get; set; } = string.Empty;

    /// <summary>
    /// Last video ID đã gửi thông báo, keyed theo YouTube Channel ID.
    /// Hỗ trợ multi-channel.
    /// </summary>
    public Dictionary<string, string> LastVideoIds { get; set; } = new();

    public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;
}
