namespace YouTubeDiscordBot.Models;

public class BotState
{
    public string LastVideoId { get; set; } = string.Empty;
    public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;
}
