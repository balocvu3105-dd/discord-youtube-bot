namespace YouTubeDiscordBot.Config;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";
    public string BotToken { get; set; } = string.Empty;
    public ulong ChannelId { get; set; }
    public string YouTubeApiKey { get; set; } = string.Empty;
    public string YouTubeChannelId { get; set; } = string.Empty;
    public int CheckIntervalSeconds { get; set; } = 60;
    public string StateFilePath { get; set; } = "last_video_state.json";
}