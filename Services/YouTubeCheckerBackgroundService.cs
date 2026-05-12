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

    // =========================================================
    // MAIN LOOP
    // =========================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 YouTube checker started");

        var state = await _persistence.LoadStateAsync();
        _lastKnownVideoId = state.LastVideoId ?? string.Empty;

        _liveStateCache = await _liveStateService.LoadAsync();
        _logger.LogInformation("Loaded live state: {Count} items", _liveStateCache.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Chỉ dùng RSS — 0 quota, detect được cả video lẫn live
                await CheckForNewVideoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Main loop error");
            }

            int delay = _config.CheckIntervalSeconds;
            _logger.LogInformation("⏱ Next check in {Seconds}s", delay);

            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }

    // =========================================================
    // CHECK NEW VIDEO / LIVE FROM RSS
    // =========================================================

    private async Task CheckForNewVideoAsync()
    {
        var apiIds = await _youtubeApi.GetLatestVideoIdsFromApiAsync();

        if (apiIds == null || apiIds.Count == 0)
        {
            _logger.LogWarning("⚠️ RSS returned 0 videos");
            return;
        }

        // INIT: lần đầu chạy, chỉ lưu ID, không gửi
        if (string.IsNullOrWhiteSpace(_lastKnownVideoId))
        {
            _lastKnownVideoId = apiIds[0];
            await _persistence.SaveStateAsync(new BotState { LastVideoId = _lastKnownVideoId });
            _logger.LogInformation("🔖 Init last video = {Id}", _lastKnownVideoId);
            return;
        }

        // Tìm các ID mới hơn _lastKnownVideoId
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

        // Reverse: gửi từ cũ → mới
        newIds.Reverse();

        foreach (var id in newIds)
        {
            var video = await _youtubeApi.GetVideoByIdAsync(id);
            if (video == null) continue;

            string currentState = video.LiveBroadcastContent;
            _logger.LogInformation("🎥 Detect: {Title} | state={State}", video.Title, currentState);

            // -------------------------------------------------
            // LIVESTREAM
            // -------------------------------------------------
            if (currentState == "live")
            {
                if (IsLiveAlreadyNotified(video.VideoId))
                {
                    _logger.LogInformation("⏭ Live already notified: {Title}", video.Title);
                    continue;
                }

                _logger.LogInformation("🔴 LIVE DETECTED: {Title}", video.Title);
                await _discordService.SendVideoNotificationAsync(video);
                MarkLiveNotified(video.VideoId);
                continue;
            }

            // -------------------------------------------------
            // VIDEO THƯỜNG
            // -------------------------------------------------
            if (currentState == "none")
            {
                if (_liveStateCache.TryGetValue(video.VideoId, out var prevState)
                    && prevState == "video_sent")
                {
                    _logger.LogInformation("⏭ Video already sent: {Title}", video.Title);
                    continue;
                }

                _logger.LogInformation("📺 NEW VIDEO: {Title}", video.Title);
                await _discordService.SendVideoNotificationAsync(video);
                _liveStateCache[video.VideoId] = "video_sent";
                continue;
            }

            // upcoming hoặc state khác: bỏ qua
            _logger.LogInformation("⏭ Skip state={State}: {Title}", currentState, video.Title);
        }

        await _liveStateService.SaveAsync(_liveStateCache);

        _lastKnownVideoId = newIds.Last();
        await _persistence.SaveStateAsync(new BotState { LastVideoId = _lastKnownVideoId });
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private bool IsLiveAlreadyNotified(string videoId)
    {
        if (!_liveStateCache.TryGetValue(videoId, out var cachedState))
            return false;

        if (!cachedState.StartsWith("live_notified_"))
            return false;

        var timestampStr = cachedState.Replace("live_notified_", "");

        if (!DateTime.TryParse(timestampStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var notifiedAt))
            return true; // parse fail → coi như đã notify

        return (DateTime.UtcNow - notifiedAt).TotalMinutes < 240; // cooldown 4 tiếng
    }

    private void MarkLiveNotified(string videoId)
    {
        _liveStateCache[videoId] = "live_notified_" + DateTime.UtcNow.ToString("o");
        _logger.LogInformation("✅ Marked live_notified: {VideoId}", videoId);
    }
}