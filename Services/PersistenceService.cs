using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class PersistenceService
{
    private readonly string _stateFilePath;
    private readonly ILogger<PersistenceService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PersistenceService(IOptions<BotConfiguration> config, ILogger<PersistenceService> logger)
    {
        _stateFilePath = config.Value.StateFilePath;
        _logger = logger;
    }

    public async Task<BotState> LoadStateAsync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("Chưa có file state. Bắt đầu mới.");
                return new BotState();
            }

            var json = await File.ReadAllTextAsync(_stateFilePath);
            var state = JsonSerializer.Deserialize<BotState>(json);
            if (state != null)
            {
                _logger.LogInformation("Đã tải state. ID video cuối: '{LastVideoId}'", state.LastVideoId);
                return state;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải state. Bắt đầu mới.");
        }
        return new BotState();
    }

    public async Task SaveStateAsync(BotState state)
    {
        try
        {
            state.LastCheckedUtc = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_stateFilePath, json);
            _logger.LogDebug("Đã lưu state. ID video cuối: '{LastVideoId}'", state.LastVideoId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu state.");
        }
    }
}