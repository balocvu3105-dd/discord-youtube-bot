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

    // persist live state
    private Dictionary<string, string> _liveStateCache = new();

    // anti duplicate live
    private readonly Dictionary<string, DateTime> _liveCooldown = new();

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
        _logger.LogInformation("🚀 YouTube checker started");

        var state = await _persistence.LoadStateAsync();

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

                await CheckCurrentLiveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Main loop error");
            }

            // 5 phút
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
    // NEW VIDEO CHECK
    // =========================================================

    private async Task CheckForNewVideoAsync()
    {
        var apiIds =
            await _youtubeApi.GetLatestVideoIdsFromApiAsync();

        if (apiIds == null || apiIds.Count == 0)
        {
            _logger.LogWarning(
                "⚠️ RSS returned 0 videos");

            return;
        }

        // INIT
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
            // anti duplicate
            if (id == _lastKnownVideoId)
            {
                _logger.LogInformation(
                    "⏭ Duplicate skipped: {Id}",
                    id);

                continue;
            }

            var video =
                await _youtubeApi.GetVideoByIdAsync(id);

            if (video == null)
                continue;

            var currentState =
                video.LiveBroadcastContent;

            _liveStateCache.TryGetValue(
                video.VideoId,
                out var previousState);

            _logger.LogDebug(
                "Video: {Title} | Prev: {Prev} -> Now: {Now}",
                video.Title,
                previousState ?? "null",
                currentState);

            // FIRST DETECT
            if (previousState == null)
            {
                _liveStateCache[video.VideoId] =
                    currentState;

                if (currentState == "live")
                {
                    _logger.LogInformation(
                        "🔴 LIVE FIRST DETECT: {Title}",
                        video.Title);

                    await _discordService
                        .SendVideoNotificationAsync(video);

                    _liveCooldown[video.VideoId] =
                        DateTime.UtcNow;
                }
                else if (currentState == "none")
                {
                    _logger.LogInformation(
                        "📹 NEW VIDEO: {Title}",
                        video.Title);

                    await _discordService
                        .SendVideoNotificationAsync(video);
                }

                continue;
            }

            // transition -> live
            if (currentState == "live"
                && previousState != "live")
            {
                if (_liveCooldown.TryGetValue(
                        video.VideoId,
                        out var lastSent))
                {
                    if ((DateTime.UtcNow - lastSent)
                        .TotalMinutes < 90)
                    {
                        _logger.LogInformation(
                            "⏳ Cooldown skip: {Title}",
                            video.Title);

                        continue;
                    }
                }

                _logger.LogInformation(
                    "🔴 LIVE DETECTED: {Title}",
                    video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveCooldown[video.VideoId] =
                    DateTime.UtcNow;

                _liveStateCache[video.VideoId] =
                    "live_notified_" +
                    DateTime.UtcNow.ToString("o");

                await _liveStateService
                    .SaveAsync(_liveStateCache);
            }

            _liveStateCache[video.VideoId] =
                currentState;
        }

        await _liveStateService
            .SaveAsync(_liveStateCache);

        _lastKnownVideoId =
            newIds.Last();

        await _persistence.SaveStateAsync(
            new BotState
            {
                LastVideoId = _lastKnownVideoId
            });
    }

    // =========================================================
    // CURRENT LIVE CHECK
    // =========================================================

    private async Task CheckCurrentLiveAsync()
    {
        var live =
            await _youtubeApi.GetCurrentLiveAsync();

        if (live == null)
        {
            _logger.LogInformation(
                "📴 No active livestream");

            return;
        }

        // cache cooldown
        if (_liveStateCache.TryGetValue(
                live.VideoId,
                out var cachedState)
            && cachedState.StartsWith("live_notified_"))
        {
            if (DateTime.TryParse(
                    cachedState.Replace(
                        "live_notified_",
                        ""),
                    out var notifiedAt))
            {
                if ((DateTime.UtcNow - notifiedAt)
                    .TotalMinutes < 90)
                {
                    _logger.LogInformation(
                        "⏳ Cache cooldown active: {Title}",
                        live.Title);

                    return;
                }
            }
        }

        // ram cooldown
        if (_liveCooldown.TryGetValue(
                live.VideoId,
                out var lastSent))
        {
            if ((DateTime.UtcNow - lastSent)
                .TotalMinutes < 90)
            {
                _logger.LogInformation(
                    "⏳ RAM cooldown active: {Title}",
                    live.Title);

                return;
            }
        }

        _logger.LogInformation(
            "🔴 LIVE DETECTED via current live check: {Title}",
            live.Title);

        await _discordService
            .SendVideoNotificationAsync(live);

        _liveCooldown[live.VideoId] =
            DateTime.UtcNow;

        _liveStateCache[live.VideoId] =
            "live_notified_" +
            DateTime.UtcNow.ToString("o");

        await _liveStateService
            .SaveAsync(_liveStateCache);
    }
}