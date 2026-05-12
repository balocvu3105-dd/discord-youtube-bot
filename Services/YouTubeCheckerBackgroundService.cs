using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class YouTubeCheckerBackgroundService : BackgroundService
{
    private readonly YouTubeApiService _youtubeApi;
    private readonly DiscordService _discordService;
    private readonly PersistenceService _persistence;
    private readonly LiveStateService _liveStateService;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeCheckerBackgroundService> _logger;

    private string _lastKnownVideoId = string.Empty;

    private Dictionary<string, string> _liveStateCache = new();

    public YouTubeCheckerBackgroundService(
        YouTubeApiService youtubeApi,
        DiscordService discordService,
        PersistenceService persistence,
        LiveStateService liveStateService,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeCheckerBackgroundService> logger)
    {
        _youtubeApi = youtubeApi;
        _discordService = discordService;
        _persistence = persistence;
        _liveStateService = liveStateService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "🚀 YouTube checker started");

        var state =
            await _persistence.LoadStateAsync();

        _lastKnownVideoId =
            state.LastVideoId ?? string.Empty;

        _liveStateCache =
            await _liveStateService.LoadAsync();

        _logger.LogInformation(
            "Loaded live state: {Count} items",
            _liveStateCache.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForNewVideoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Main loop error");
            }

            int delay =
                _config.CheckIntervalSeconds;

            _logger.LogInformation(
                "⏱ Next check in {Seconds}s",
                delay);

            await Task.Delay(
                TimeSpan.FromSeconds(delay),
                stoppingToken);
        }
    }

    private async Task CheckForNewVideoAsync()
    {
        var apiIds =
            await _youtubeApi
                .GetLatestVideoIdsFromApiAsync();

        if (apiIds == null || apiIds.Count == 0)
        {
            _logger.LogWarning(
                "⚠️ API returned 0 videos");

            return;
        }

        if (string.IsNullOrWhiteSpace(_lastKnownVideoId))
        {
            _lastKnownVideoId = apiIds[0];

            await _persistence.SaveStateAsync(
                new BotState
                {
                    LastVideoId = _lastKnownVideoId
                });

            _logger.LogInformation(
                "🔖 Init last video = {Id}",
                _lastKnownVideoId);

            return;
        }

        var newIds = new List<string>();

        foreach (var id in apiIds)
        {
            if (id == _lastKnownVideoId)
                break;

            newIds.Add(id);
        }

        if (newIds.Count == 0)
        {
            _logger.LogInformation(
                "✅ No new videos");

            return;
        }

        newIds.Reverse();

        foreach (var id in newIds)
        {
            var video =
                await _youtubeApi
                    .GetVideoByIdAsync(id);

            if (video == null)
                continue;

            var currentState =
                video.LiveBroadcastContent
                    ?.ToLower() ?? "none";

            _logger.LogInformation(
                "🎥 Detect: {Title} | state={State}",
                video.Title,
                currentState);

            // Already sent?
            if (_liveStateCache.ContainsKey(video.VideoId))
            {
                _logger.LogInformation(
                    "⏭ Already notified: {Title}",
                    video.Title);

                continue;
            }

            // LIVE
            if (currentState == "live")
            {
                _logger.LogInformation(
                    "🔴 LIVE DETECTED: {Title}",
                    video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveStateCache[video.VideoId] =
                    "live_sent";

                await _liveStateService
                    .SaveAsync(_liveStateCache);

                continue;
            }

            // NORMAL VIDEO
            if (currentState == "none")
            {
                _logger.LogInformation(
                    "📺 NEW VIDEO: {Title}",
                    video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveStateCache[video.VideoId] =
                    "video_sent";

                await _liveStateService
                    .SaveAsync(_liveStateCache);

                continue;
            }

            _logger.LogInformation(
                "⏭ Skip state={State}: {Title}",
                currentState,
                video.Title);
        }

        _lastKnownVideoId = newIds.Last();

        await _persistence.SaveStateAsync(
            new BotState
            {
                LastVideoId = _lastKnownVideoId
            });
    }
}