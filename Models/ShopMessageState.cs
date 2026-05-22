namespace YouTubeDiscordBot.Models;

public class ShopMessageState
{
    public ulong PinnedMessageId { get; set; }

    public Dictionary<string, ulong> GameMessageIds { get; set; }
        = new();
}