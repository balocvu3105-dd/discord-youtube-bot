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
        // Đảm bảo đường dẫn không bị null
        _stateFilePath = config.Value.StateFilePath ?? "last_video_state.json";
        _logger = logger;
    }

    public async Task<BotState> LoadStateAsync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("State file not found. Initializing new state.");
                return new BotState();
            }

            var json = await File.ReadAllTextAsync(_stateFilePath);

            // Kiểm tra nếu file rỗng
            if (string.IsNullOrWhiteSpace(json)) return new BotState();

            var state = JsonSerializer.Deserialize<BotState>(json);
            if (state != null)
            {
                _logger.LogInformation("State loaded. Last Video ID: '{LastVideoId}'", state.LastVideoId);
                return state;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state. Starting fresh.");
        }
        return new BotState();
    }

    public async Task SaveStateAsync(BotState state)
    {
        try
        {
            // Đảm bảo thư mục chứa file tồn tại (Tránh lỗi DirectoryNotFoundException)
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            state.LastCheckedUtc = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(state, JsonOptions);

            // Sử dụng WriteAllTextAsync để ghi đè file cũ bằng ID video mới nhất
            await File.WriteAllTextAsync(_stateFilePath, json);
            _logger.LogInformation("State saved successfully. Last Video ID: '{LastVideoId}'", state.LastVideoId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to '{Path}'", _stateFilePath);
        }
    }
}