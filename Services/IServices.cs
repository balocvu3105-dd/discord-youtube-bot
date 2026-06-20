using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public interface IDiscordService
{
    Task ConnectAsync();
    Task WaitForReadyAsync();
    Task SendVideoNotificationAsync(VideoInfo video);
    Task SendToChannelAsync(ulong channelId, string? text = null,
        Discord.Embed? embed = null,
        Discord.MessageComponent? components = null,
        Discord.AllowedMentions? allowedMentions = null);
    Discord.WebSocket.DiscordSocketClient Client { get; }
}

public interface IPersistenceService
{
    Task<BotState> LoadStateAsync();
    Task SaveStateAsync(BotState state);
}

public interface ILiveStateService
{
    Task<Dictionary<string, string>> LoadAsync();
    Task SaveAsync(Dictionary<string, string> state);
}

public interface IYouTubeApiService
{
    Task<List<string>> GetLatestVideoIdsAsync(string channelId);
    Task<VideoInfo?> GetVideoByIdAsync(string videoId);
}

/// <summary>Build Discord embed cho shop.</summary>
public interface IShopService
{
    Task WarmDiscountCacheAsync();

    /// <summary>Build embed section LDShop — hiển thị tất cả game + buttons.</summary>
    Task<(Discord.Embed embed, Discord.MessageComponent components)> BuildLdShopEmbedAsync();

    /// <summary>Build embed section Lootbar — hiển thị tất cả game + buttons.</summary>
    Task<(Discord.Embed embed, Discord.MessageComponent components)> BuildLootbarEmbedAsync();
}

public interface IShopMessagePersistenceService
{
    Task<ShopMessageState> LoadAsync();
    Task SaveAsync(ShopMessageState state);
}