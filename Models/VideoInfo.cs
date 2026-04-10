namespace YouTubeDiscordBot.Models;

public class VideoInfo
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    // Thay đổi từ => thành { get; set; } để code gán được giá trị vào
    public string Url { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string LiveBroadcastContent { get; set; } = "none";
    public bool IsLivestream =>
        LiveBroadcastContent == "live" || LiveBroadcastContent == "upcoming";
}