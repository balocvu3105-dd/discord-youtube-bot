namespace YouTubeDiscordBot.Models;

public class VideoInfo
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string LiveBroadcastContent { get; set; } = "none";

    /// <summary>
    /// True chi khi dang live that su (status = "live").
    /// Dung de quyet dinh channel/role/text khi gui thong bao.
    /// Upcoming KHONG phai live — dung LiveBroadcastContent == "upcoming" de check rieng.
    /// </summary>
    public bool IsLive => LiveBroadcastContent == "live";
}
