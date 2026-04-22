using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YouTubeDiscordBot.Services;

public class LiveStateService
{
    private readonly string _filePath = "live_state.json";
    private readonly ILogger<LiveStateService> _logger;

    public LiveStateService(ILogger<LiveStateService> logger)
    {
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new Dictionary<string, string>();

            var json = await File.ReadAllTextAsync(_filePath);

            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>();

            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            return data ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load live state fail");
            return new Dictionary<string, string>();
        }
    }

    public async Task SaveAsync(Dictionary<string, string> state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save live state fail");
        }
    }
}