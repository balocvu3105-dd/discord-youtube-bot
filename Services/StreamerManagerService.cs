using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class StreamerManagerService : AsyncJsonStore<UnifiedStreamState>
{
    private readonly BotConfiguration _config;
    private readonly ILogger<StreamerManagerService> _logger;

    protected override string FilePath => "data/unified_stream_state.json";
    protected override ILogger Logger => _logger;

    public StreamerManagerService(IOptions<BotConfiguration> config, ILogger<StreamerManagerService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<UnifiedStreamState> LoadStateAsync() => await ReadAsync();
    public async Task SaveStateAsync(UnifiedStreamState state) => await WriteAsync(state);

    /// <summary>
    /// Lấy toàn bộ danh sách kênh cần theo dõi, kết hợp giữa appsettings.json và các kênh được thêm động.
    /// </summary>
    public async Task<List<StreamerConfigItem>> GetAllTrackedStreamersAsync()
    {
        var list = new List<StreamerConfigItem>();

        // 1. Kênh từ appsettings.json (giữ tương thích ngược 100%)
        foreach (var yt in _config.YoutubeChannelIds)
        {
            if (!string.IsNullOrWhiteSpace(yt) && !list.Any(x => x.Platform == "YouTube" && x.UsernameOrId == yt))
                list.Add(new StreamerConfigItem { Platform = "YouTube", UsernameOrId = yt, CustomName = $"YouTube ({yt})" });
        }

        foreach (var tk in _config.TikTokUsernames)
        {
            if (!string.IsNullOrWhiteSpace(tk) && !list.Any(x => x.Platform == "TikTok" && x.UsernameOrId == tk))
                list.Add(new StreamerConfigItem { Platform = "TikTok", UsernameOrId = tk, CustomName = $"@{tk}" });
        }

        foreach (var tw in _config.TwitchUsernames)
        {
            if (!string.IsNullOrWhiteSpace(tw) && !list.Any(x => x.Platform == "Twitch" && x.UsernameOrId == tw))
                list.Add(new StreamerConfigItem { Platform = "Twitch", UsernameOrId = tw, CustomName = $"{tw}" });
        }

        foreach (var kc in _config.KickSlugs)
        {
            if (!string.IsNullOrWhiteSpace(kc) && !list.Any(x => x.Platform == "Kick" && x.UsernameOrId == kc))
                list.Add(new StreamerConfigItem { Platform = "Kick", UsernameOrId = kc, CustomName = $"{kc}" });
        }

        foreach (var fb in _config.FacebookPages)
        {
            if (!string.IsNullOrWhiteSpace(fb) && !list.Any(x => x.Platform == "Facebook" && x.UsernameOrId == fb))
                list.Add(new StreamerConfigItem { Platform = "Facebook", UsernameOrId = fb, CustomName = $"{fb}" });
        }

        // 2. Kênh được thêm động từ Discord slash command /live add
        var state = await LoadStateAsync();
        foreach (var dyn in state.DynamicStreamers)
        {
            if (!list.Any(x => string.Equals(x.Key, dyn.Key, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(dyn);
            }
        }

        return list;
    }

    /// <summary>Thêm một kênh theo dõi mới qua Discord slash command.</summary>
    public async Task<bool> AddDynamicStreamerAsync(StreamerConfigItem item)
    {
        var state = await LoadStateAsync();
        if (state.DynamicStreamers.Any(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase)))
        {
            return false; // Đã tồn tại
        }

        state.DynamicStreamers.Add(item);
        await SaveStateAsync(state);
        _logger.LogInformation("Đã thêm kênh theo dõi động: {Platform} - {User}", item.Platform, item.UsernameOrId);
        return true;
    }

    /// <summary>Xóa một kênh theo dõi động qua Discord slash command.</summary>
    public async Task<bool> RemoveDynamicStreamerAsync(string platform, string usernameOrId)
    {
        var state = await LoadStateAsync();
        var key = $"{platform}_{usernameOrId}".ToLowerInvariant();
        var removed = state.DynamicStreamers.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed)
        {
            state.LiveNotifiedStates.Remove(key);
            state.LastStreamIds.Remove(key);
            await SaveStateAsync(state);
            _logger.LogInformation("Đã xóa kênh theo dõi động: {Key}", key);
        }

        return removed;
    }
}
