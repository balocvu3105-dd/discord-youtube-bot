using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Wrapper để serialize Dictionary&lt;string,string&gt; vào JSON.
/// JsonSerializer cần một class, không thể serialize Dictionary trực tiếp
/// vào AsyncJsonStore vì generic constraint cần new().
/// </summary>
file class LiveStateWrapper
{
    public Dictionary<string, string> States { get; set; } = new();
}

/// <summary>
/// Lưu/đọc live state cache.
/// Key = YouTube video ID, Value = "upcoming" | "live_sent" | "video_sent"
///
/// FIX so với code cũ:
///   - Path giờ lấy từ config (LiveStateFilePath) thay vì hardcode
///     → file nằm trong data/ → được mount vào Docker volume → không mất khi restart
///   - Thread-safe qua AsyncJsonStore
/// </summary>
public class LiveStateService : AsyncJsonStore<LiveStateWrapper>, ILiveStateService
{
    private readonly string _filePath;
    private readonly ILogger<LiveStateService> _logger;

    protected override string FilePath => _filePath;
    protected override ILogger Logger => _logger;

    public LiveStateService(
        IOptions<BotConfiguration> config,
        ILogger<LiveStateService> logger)
    {
        _filePath = config.Value.LiveStateFilePath;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> LoadAsync()
    {
        var wrapper = await ReadAsync();
        _logger.LogInformation("Live state loaded — {Count} entries", wrapper.States.Count);
        return wrapper.States;
    }

    public async Task SaveAsync(Dictionary<string, string> state)
    {
        await WriteAsync(new LiveStateWrapper { States = state });
    }
}