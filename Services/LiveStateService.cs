using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

// Bỏ keyword "file" — đổi thành internal
public class LiveStateWrapper
{
    public Dictionary<string, string> States { get; set; } = new();
}

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