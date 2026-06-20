using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Lưu/đọc BotState (last video ID đã xử lý).
/// Kế thừa AsyncJsonStore để có thread-safe read/write tự động.
/// </summary>
public class PersistenceService : AsyncJsonStore<BotState>, IPersistenceService
{
    private readonly string _filePath;
    private readonly ILogger<PersistenceService> _logger;

    protected override string FilePath => _filePath;
    protected override ILogger Logger => _logger;

    public PersistenceService(
        IOptions<BotConfiguration> config,
        ILogger<PersistenceService> logger)
    {
        _filePath = config.Value.StateFilePath;
        _logger = logger;
    }

    public async Task<BotState> LoadStateAsync()
    {
        var state = await ReadAsync();
        _logger.LogInformation("State loaded — LastVideoId: {Id}", state.LastVideoId);
        return state;
    }

    public async Task SaveStateAsync(BotState state)
    {
        state.LastCheckedUtc = DateTime.UtcNow;
        await WriteAsync(state);
        _logger.LogInformation("State saved — LastVideoId: {Id}", state.LastVideoId);
    }
}