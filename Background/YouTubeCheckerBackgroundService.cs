using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Background;

public class YouTubeCheckerBackgroundService : BackgroundService
{
    private readonly IDiscordService _discord;
    private readonly DiscordService _discordImpl;
    private readonly IYouTubeApiService _youtube;
    private readonly IPersistenceService _persistence;
    private readonly ILiveStateService _liveState;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeCheckerBackgroundService> _logger;

    public YouTubeCheckerBackgroundService(
        IDiscordService discord,
        DiscordService discordImpl,
        IYouTubeApiService youtube,
        IPersistenceService persistence,
        ILiveStateService liveState,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeCheckerBackgroundService> logger)
    {
        _discord = discord;
        _discordImpl = discordImpl;
        _youtube = youtube;
        _persistence = persistence;
        _liveState = liveState;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "YouTubeCheckerBackgroundService starting — Interval={Seconds}s",
            _config.CheckIntervalSeconds);

        await _discordImpl.WaitForReadyAsync();
        _logger.LogInformation("Discord ready — YouTubeCheckerBackgroundService running");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckYouTubeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "YouTubeCheckerBackgroundService — unhandled exception");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.CheckIntervalSeconds), stoppingToken);
        }
    }

    private async Task CheckYouTubeAsync(CancellationToken ct)
    {
        var videoIds = await _youtube.GetLatestVideoIdsAsync();
        if (videoIds.Count == 0) return;

        var botState = await _persistence.LoadStateAsync();
        var liveStates = await _liveState.LoadAsync();
        var stateChanged = false;
        var liveChanged = false;

        foreach (var videoId in videoIds)
        {
            if (ct.IsCancellationRequested) break;

            if (botState.LastVideoId == videoId && !liveStates.ContainsKey(videoId))
                continue;

            var video = await _youtube.GetVideoByIdAsync(videoId);
            if (video is null) continue;

            var currentStatus = liveStates.GetValueOrDefault(videoId, "none");

            if (video.LiveBroadcastContent == "live")
            {
                if (currentStatus != "live_sent" && !currentStatus.StartsWith("live_notified"))
                {
                    await _discord.SendVideoNotificationAsync(video);
                    liveStates[videoId] = $"live_notified_{DateTime.UtcNow:O}";
                    liveChanged = true;
                    _logger.LogInformation("LIVE notification sent — {VideoId}", videoId);
                }
                else
                {
                    liveStates[videoId] = "live";
                    liveChanged = true;
                }
            }
            else if (video.LiveBroadcastContent == "upcoming")
            {
                if (currentStatus == "none")
                {
                    liveStates[videoId] = "upcoming";
                    liveChanged = true;
                    _logger.LogInformation("Upcoming livestream detected — {VideoId}", videoId);
                }
            }
            else
            {
                if (botState.LastVideoId != videoId)
                {
                    await _discord.SendVideoNotificationAsync(video);
                    botState.LastVideoId = videoId;
                    stateChanged = true;
                    liveStates[videoId] = "video_sent";
                    liveChanged = true;
                    _logger.LogInformation("VIDEO notification sent — {VideoId}", videoId);
                    break;
                }
            }
        }

        if (stateChanged)
            await _persistence.SaveStateAsync(botState);

        if (liveChanged)
            await _liveState.SaveAsync(liveStates);
    }
}