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

    // Cache trạng thái đã xử lý
    // Key = VideoId, Value = "live_sent" | "video_sent" | "upcoming"
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

    // =========================================================
    // ENTRY POINT — chạy 2 task song song
    // =========================================================

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 YouTube checker started");

        var state = await _persistence.LoadStateAsync();
        _lastKnownVideoId = state.LastVideoId ?? string.Empty;
        _liveStateCache = await _liveStateService.LoadAsync();

        _logger.LogInformation(
            "Loaded live state: {Count} items",
            _liveStateCache.Count);

        // Chạy 2 vòng lặp song song, độc lập nhau
        await Task.WhenAll(
            VideoCheckLoopAsync(stoppingToken),
            LiveCheckLoopAsync(stoppingToken));
    }

    // =========================================================
    // LOOP 1 — Detect video mới (interval 120s)
    // =========================================================

    private async Task VideoCheckLoopAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForNewVideoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ VideoCheckLoop error");
            }

            _logger.LogInformation(
                "⏱ [Video] Next check in {Seconds}s",
                _config.CheckIntervalSeconds);

            await Task.Delay(
                TimeSpan.FromSeconds(_config.CheckIntervalSeconds),
                stoppingToken);
        }
    }

    // =========================================================
    // LOOP 2 — Poll các video "upcoming" xem có live chưa (30s)
    // =========================================================

    private async Task LiveCheckLoopAsync(
        CancellationToken stoppingToken)
    {
        const int liveCheckSeconds = 30;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckUpcomingForLiveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ LiveCheckLoop error");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(liveCheckSeconds),
                stoppingToken);
        }
    }

    // =========================================================
    // CHECK VIDEO MỚI
    // =========================================================

    private async Task CheckForNewVideoAsync()
    {
        var apiIds =
            await _youtubeApi.GetLatestVideoIdsFromApiAsync();

        if (apiIds == null || apiIds.Count == 0)
        {
            _logger.LogWarning("⚠️ API returned 0 videos");
            return;
        }

        // -------------------------------------------------------
        // Lần đầu chạy — chưa có lastKnownVideoId
        // Check state video mới nhất ngay lúc init
        // -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(_lastKnownVideoId))
        {
            var latestVideo =
                await _youtubeApi.GetVideoByIdAsync(apiIds[0]);

            if (latestVideo != null)
            {
                var initState =
                    latestVideo.LiveBroadcastContent?.ToLower()
                    ?? "none";

                _logger.LogInformation(
                    "🔖 Init: {Id} | state={State} | {Title}",
                    latestVideo.VideoId,
                    initState,
                    latestVideo.Title);

                if (initState == "live")
                {
                    // Đang live ngay lúc bot khởi động
                    _logger.LogInformation(
                        "🔴 LIVE ON STARTUP: {Title}",
                        latestVideo.Title);

                    await _discordService
                        .SendVideoNotificationAsync(latestVideo);

                    _liveStateCache[latestVideo.VideoId] =
                        "live_sent";

                    await _liveStateService
                        .SaveAsync(_liveStateCache);
                }
                else if (initState == "upcoming")
                {
                    // Scheduled live → LiveCheckLoop theo dõi tiếp
                    _logger.LogInformation(
                        "🕐 UPCOMING ON STARTUP: {Title}",
                        latestVideo.Title);

                    _liveStateCache[latestVideo.VideoId] =
                        "upcoming";

                    await _liveStateService
                        .SaveAsync(_liveStateCache);
                }
                // "none" = video thường đã đăng từ trước, bỏ qua
            }

            _lastKnownVideoId = apiIds[0];

            await _persistence.SaveStateAsync(
                new BotState { LastVideoId = _lastKnownVideoId });

            return;
        }

        // -------------------------------------------------------
        // Tìm các video mới hơn lastKnownVideoId
        // -------------------------------------------------------
        var newIds = new List<string>();
        foreach (var id in apiIds)
        {
            if (id == _lastKnownVideoId) break;
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
            var video =
                await _youtubeApi.GetVideoByIdAsync(id);

            if (video == null) continue;

            var state =
                video.LiveBroadcastContent?.ToLower() ?? "none";

            _logger.LogInformation(
                "🎥 Detect: {Title} | state={State}",
                video.Title, state);

            // Đã xử lý rồi thì bỏ qua
            if (_liveStateCache.ContainsKey(video.VideoId))
            {
                _logger.LogInformation(
                    "⏭ Already notified: {Title}", video.Title);
                continue;
            }

            if (state == "live")
            {
                _logger.LogInformation(
                    "🔴 LIVE DETECTED: {Title}", video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveStateCache[video.VideoId] = "live_sent";
                await _liveStateService.SaveAsync(_liveStateCache);
            }
            else if (state == "none")
            {
                _logger.LogInformation(
                    "📺 NEW VIDEO: {Title}", video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveStateCache[video.VideoId] = "video_sent";
                await _liveStateService.SaveAsync(_liveStateCache);
            }
            else if (state == "upcoming")
            {
                _logger.LogInformation(
                    "🕐 UPCOMING: {Title} — sẽ theo dõi tiếp",
                    video.Title);

                _liveStateCache[video.VideoId] = "upcoming";
                await _liveStateService.SaveAsync(_liveStateCache);
            }
        }

        _lastKnownVideoId = newIds.Last();

        await _persistence.SaveStateAsync(
            new BotState { LastVideoId = _lastKnownVideoId });
    }

    // =========================================================
    // CHECK UPCOMING → LIVE (chạy mỗi 30s)
    // =========================================================

    private async Task CheckUpcomingForLiveAsync()
    {
        var upcomingIds = _liveStateCache
            .Where(kv => kv.Value == "upcoming")
            .Select(kv => kv.Key)
            .ToList();

        if (upcomingIds.Count == 0)
            return;

        _logger.LogInformation(
            "🔍 [LiveCheck] Checking {Count} upcoming video(s)...",
            upcomingIds.Count);

        foreach (var id in upcomingIds)
        {
            var video =
                await _youtubeApi.GetVideoByIdAsync(id);

            if (video == null) continue;

            var state =
                video.LiveBroadcastContent?.ToLower() ?? "none";

            _logger.LogInformation(
                "🔍 [LiveCheck] {Id} → state={State}", id, state);

            if (state == "live")
            {
                _logger.LogInformation(
                    "🔴 LIVE NOW: {Title}", video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveStateCache[video.VideoId] = "live_sent";
                await _liveStateService.SaveAsync(_liveStateCache);
            }
            else if (state == "none")
            {
                // Live kết thúc mà chưa kịp gửi
                _logger.LogInformation(
                    "📺 LIVE ENDED (missed): {Title}", video.Title);

                await _discordService
                    .SendVideoNotificationAsync(video);

                _liveStateCache[video.VideoId] = "video_sent";
                await _liveStateService.SaveAsync(_liveStateCache);
            }
            // "upcoming" → giữ nguyên, check lại lần sau
        }
    }
}