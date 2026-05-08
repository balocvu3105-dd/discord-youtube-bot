namespace YouTubeDiscordBot.Config;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    public string DiscordToken { get; set; } = string.Empty;
    public string ChannelName { get; set; } = "youtube-noti";
    public string YoutubeApiKey { get; set; } = string.Empty;
    public string YoutubeChannelId { get; set; } = string.Empty;
    public int CheckIntervalSeconds { get; set; } = 1200;
    public string StateFilePath { get; set; } = "last_video_state.json";
    public ulong DiscordChannelId { get; set; }

    public string PromoChannelName { get; set; } = "khuyen-mai";
    public int PromoIntervalHours { get; set; } = 12;
    public List<PromoGameConfig> PromoGames { get; set; } = new();
}

public class PromoGameConfig
{
    public string Name { get; set; } = string.Empty;
    public string AffiliateLink { get; set; } = string.Empty;
}