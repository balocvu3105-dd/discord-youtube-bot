namespace YouTubeDiscordBot.Models;

public class VideoInfo
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string LiveBroadcastContent { get; set; } = "none";
    public bool IsLive =>
        LiveBroadcastContent == "live" || LiveBroadcastContent == "upcoming";
}