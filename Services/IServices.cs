using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

// ── WHY INTERFACES? ──────────────────────────────────────────────────────────
// Interface tách "contract" (API) khỏi "implementation" (logic).
// Lợi ích:
//   1. Unit test dễ hơn — mock interface thay vì mock class thật
//   2. Swap implementation dễ hơn (vd: đổi JSON → SQLite sau này)
//   3. Đọc code dễ hơn — chỉ cần nhìn interface để biết service làm gì
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Quản lý kết nối Discord và gửi message.</summary>
public interface IDiscordService
{
	Task ConnectAsync();
	Task SendVideoNotificationAsync(VideoInfo video);
	Task SendToChannelAsync(ulong channelId, string? text = null,
		Discord.Embed? embed = null,
		Discord.MessageComponent? components = null,
		Discord.AllowedMentions? allowedMentions = null);
	Discord.WebSocket.DiscordSocketClient Client { get; }
}

/// <summary>Đọc/ghi BotState (last video ID).</summary>
public interface IPersistenceService
{
	Task<BotState> LoadStateAsync();
	Task SaveStateAsync(BotState state);
}

/// <summary>Đọc/ghi live state cache (dictionary video ID → trạng thái).</summary>
public interface ILiveStateService
{
	Task<Dictionary<string, string>> LoadAsync();
	Task SaveAsync(Dictionary<string, string> state);
}

/// <summary>Tương tác với YouTube Data API v3.</summary>
public interface IYouTubeApiService
{
	Task<List<string>> GetLatestVideoIdsAsync();
	Task<VideoInfo?> GetVideoByIdAsync(string videoId);
}

/// <summary>Build Discord embed cho shop.</summary>
public interface IShopService
{
	(Discord.Embed embed, Discord.MessageComponent components) BuildOverview();
	(Discord.Embed embed, Discord.MessageComponent components)? BuildGameEmbed(ShopGameConfig game);
}

/// <summary>Đọc/ghi ShopMessageState.</summary>
public interface IShopMessagePersistenceService
{
	Task<ShopMessageState> LoadAsync();
	Task SaveAsync(ShopMessageState state);
}