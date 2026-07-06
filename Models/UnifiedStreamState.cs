namespace YouTubeDiscordBot.Models;

/// <summary>
/// Trạng thái lưu xuống đĩa (JSON) của các kênh đang theo dõi đa nền tảng.
/// </summary>
public class UnifiedStreamState
{
    /// <summary>
    /// Key: Platform_UsernameOrId (lowercase).
    /// Value: Trạng thái đã gửi thông báo chưa (true = đã gửi thông báo cho stream hiện tại, false = offline).
    /// </summary>
    public Dictionary<string, bool> LiveNotifiedStates { get; set; } = new();

    /// <summary>
    /// Key: Platform_UsernameOrId (lowercase).
    /// Value: Stream ID hoặc Video ID mới nhất đã thông báo (tránh gửi lặp lại nếu API mạng glitch).
    /// </summary>
    public Dictionary<string, string> LastStreamIds { get; set; } = new();

    /// <summary>Danh sách các streamer được thêm động qua Discord slash command /live add.</summary>
    public List<StreamerConfigItem> DynamicStreamers { get; set; } = new();
}
