
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

    // persist live state (upcoming / live / none)
    private Dictionary<string, string> _liveStateCache = new();

    // anti-flicker: tránh spam notify cùng 1 video
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Service start");

        var state = await _persistence.LoadStateAsync();
        _lastKnownVideoId = state.LastVideoId ?? "";

        _liveStateCache = await _liveStateService.LoadAsync();

        _logger.LogInformation(
            "Loaded live state: {Count} items",
            _liveStateCache.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check video mới + live transition
                await CheckForNewVideoAsync();

                // Check livestream hiện tại
                await CheckCurrentLiveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi loop chính");
            }

            // tối thiểu 30 phút
            int delay = Math.Max(_config.CheckIntervalSeconds, 900);

            _logger.LogInformation(
                "⏱ Next check in {Seconds}s",
                delay);

            await Task.Delay(
                TimeSpan.FromSeconds(delay),
                stoppingToken);
        }
    }

    // ========================= CHECK NEW VIDEO =========================

    private async Task CheckForNewVideoAsync()
    {
        var apiIds = await _youtubeApi.GetLatestVideoIdsFromApiAsync();

        if (apiIds == null || apiIds.Count == 0)
            return;

        // INIT
        if (string.IsNullOrEmpty(_lastKnownVideoId))
        {
            _lastKnownVideoId = apiIds[0];

            await _persistence.SaveStateAsync(new BotState
            {
                LastVideoId = _lastKnownVideoId
            });

            _logger.LogInformation(
                "🔖 Init: set lastKnownVideoId = {Id}",
                _lastKnownVideoId);

            var initVideo =
                await _youtubeApi.GetVideoByIdAsync(_lastKnownVideoId);

            if (initVideo != null
                && initVideo.LiveBroadcastContent == "live")
            {
                _logger.LogInformation(
                    "🔴 INIT LIVE SEND: {Title}",
                    initVideo.Title);

                await _discordService
                    .SendVideoNotificationAsync(initVideo);

                _liveCooldown[initVideo.VideoId] =
                    DateTime.UtcNow;
            }

            return;
        }

        // lấy danh sách video mới
        var newIds = new List<string>();

        foreach (var id in apiIds)
        {
            if (id == _lastKnownVideoId)
                break;

            newIds.Add(id);
        }

        if (newIds.Count == 0)
        {
            _logger.LogInformation("✅ No new videos");
            return;
        }

        newIds.Reverse();

        foreach (var id in newIds)
        {
            var video = await _youtubeApi.GetVideoByIdAsync(id);

            if (video == null)
                continue;

            var currentState = video.LiveBroadcastContent;

            _liveStateCache.TryGetValue(
                video.VideoId,
                out var previousState);

            _logger.LogInformation(
                "Video: {Title} | Prev: {Prev} -> Now: {Now}",
                video.Title,
                previousState ?? "null",
                currentState);

            // lần đầu detect video này
            if (previousState == null)
            {
                _liveStateCache[video.VideoId] = currentState;

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
                        .TotalHours < 2)
                    {
                        _logger.LogInformation(
                            "⏳ Skip cooldown: {Title}",
                            video.Title);

                        continue;
                    }
                }

                _logger.LogInformation(
                    "🔴 LIVE DETECTED via transition: {Title}",
                    video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveCooldown[video.VideoId] =
                    DateTime.UtcNow;

                // persist cooldown qua restart
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

        _lastKnownVideoId = newIds.Last();

        await _persistence.SaveStateAsync(new BotState
        {
            LastVideoId = _lastKnownVideoId
        });
    }

    // ========================= CHECK CURRENT LIVE =========================

    private async Task CheckCurrentLiveAsync()
    {
        var live = await _youtubeApi.GetCurrentLiveAsync();

        if (live == null)
        {
            _logger.LogInformation(
                "📴 No active livestream");

            return;
        }

        // persist cache
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
                    .TotalHours < 2)
                {
                    _logger.LogInformation(
                        "⏳ Live cooldown active (cache): {Title}",
                        live.Title);

                    return;
                }
            }
        }

        // RAM cooldown
        if (_liveCooldown.TryGetValue(
                live.VideoId,
                out var lastSent))
        {
            if ((DateTime.UtcNow - lastSent)
                .TotalHours < 2)
            {
                _logger.LogInformation(
                    "⏳ Live cooldown active (ram): {Title}",
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

        // persist qua restart
        _liveStateCache[live.VideoId] =
            "live_notified_" +
            DateTime.UtcNow.ToString("o");

        await _liveStateService
            .SaveAsync(_liveStateCache);
    }
}

