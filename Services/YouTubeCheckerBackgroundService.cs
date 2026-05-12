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

    private string _lastKnownVideoId =
        string.Empty;

    private Dictionary<string, string>
        _liveStateCache = new();

    public YouTubeCheckerBackgroundService(
        YouTubeApiService youtubeApi,
        DiscordService discordService,
        PersistenceService persistence,
        LiveStateService liveStateService,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeCheckerBackgroundService> logger)
    {
        _youtubeApi =
            youtubeApi;

        _discordService =
            discordService;

        _persistence =
            persistence;

        _liveStateService =
            liveStateService;

        _config =
            config.Value;

        _logger =
            logger;
    }

    // =========================================================
    // MAIN LOOP
    // =========================================================

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

        while (!stoppingToken
               .IsCancellationRequested)
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

    // =========================================================
    // CHECK NEW VIDEO / LIVE
    // =========================================================

    private async Task CheckForNewVideoAsync()
    {
        var apiIds =
            await _youtubeApi
                .GetLatestVideoIdsFromApiAsync();

        if (apiIds == null ||
            apiIds.Count == 0)
        {
            _logger.LogWarning(
                "⚠️ API returned 0 videos");

            return;
        }

        // =====================================================
        // INIT
        // =====================================================

        if (string.IsNullOrWhiteSpace(
                _lastKnownVideoId))
        {
            _lastKnownVideoId =
                apiIds[0];

            await _persistence
                .SaveStateAsync(
                    new BotState
                    {
                        LastVideoId =
                            _lastKnownVideoId
                    });

            _logger.LogInformation(
                "🔖 Init last video = {Id}",
                _lastKnownVideoId);

            return;
        }

        // =====================================================
        // FIND NEW IDS
        // =====================================================

        var newIds =
            new List<string>();

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

        // Send oldest → newest
        newIds.Reverse();

        // =====================================================
        // PROCESS VIDEOS
        // =====================================================

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

            // =================================================
            // LIVESTREAM
            // =================================================

            if (currentState == "live")
            {
                // Already notified?
                if (_liveStateCache
                    .ContainsKey(video.VideoId))
                {
                    _logger.LogInformation(
                        "⏭ Live already notified: {Title}",
                        video.Title);

                    continue;
                }

                _logger.LogInformation(
                    "🔴 LIVE DETECTED: {Title}",
                    video.Title);

                // ✅ SEND LIVE MESSAGE
                await _discordService
                    .SendLiveNotificationAsync(video);

                // ✅ SAVE STATE
                _liveStateCache[video.VideoId] =
                    "live_sent";

                await _liveStateService
                    .SaveAsync(_liveStateCache);

                _logger.LogInformation(
                    "✅ Live notification sent: {VideoId}",
                    video.VideoId);

                // ✅ IMPORTANT
                // Prevent video flow below
                continue;
            }

            // =================================================
            // NORMAL VIDEO
            // =================================================

            if (currentState == "none")
            {
                if (_liveStateCache
                    .ContainsKey(video.VideoId))
                {
                    _logger.LogInformation(
                        "⏭ Video already notified: {Title}",
                        video.Title);

                    continue;
                }

                _logger.LogInformation(
                    "📺 NEW VIDEO: {Title}",
                    video.Title);

                // ✅ SEND VIDEO MESSAGE
                await _discordService
                    .SendVideoNotificationAsync(video);

                // ✅ SAVE STATE
                _liveStateCache[video.VideoId] =
                    "video_sent";

                await _liveStateService
                    .SaveAsync(_liveStateCache);

                _logger.LogInformation(
                    "✅ Video notification sent: {VideoId}",
                    video.VideoId);

                continue;
            }

            // =================================================
            // UPCOMING / OTHER STATES
            // =================================================

            _logger.LogInformation(
                "⏭ Skip state={State}: {Title}",
                currentState,
                video.Title);
        }

        // =====================================================
        // UPDATE LAST VIDEO
        // =====================================================

        _lastKnownVideoId =
            newIds.Last();

        await _persistence.SaveStateAsync(
            new BotState
            {
                LastVideoId =
                    _lastKnownVideoId
            });

        _logger.LogInformation(
            "💾 Updated last video ID: {Id}",
            _lastKnownVideoId);
    }
}