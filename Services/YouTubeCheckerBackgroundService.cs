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

    // 🔥 persist state
    private Dictionary<string, string> _liveStateCache = new();

    // 🔥 anti flicker
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

        // 🔥 load state từ file
        _liveStateCache = await _liveStateService.LoadAsync();

        _logger.LogInformation("Loaded live state: {Count} items", _liveStateCache.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForNewVideoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi loop chính");
            }

            int delay = _config.CheckIntervalSeconds;
            if (delay < 15) delay = 15;

            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }

    private async Task CheckForNewVideoAsync()
    {
        var rssIds = await _youtubeApi.GetLatestVideoIdsFromRssAsync();

        if (rssIds == null || rssIds.Count == 0)
            return;

        // 🔒 INIT SAFE
        if (string.IsNullOrEmpty(_lastKnownVideoId))
        {
            _lastKnownVideoId = rssIds[0];

            await _persistence.SaveStateAsync(new BotState
            {
                LastVideoId = _lastKnownVideoId
            });

            _logger.LogInformation("Init RSS - không gửi");
            return;
        }

        var newIds = new List<string>();

        foreach (var id in rssIds)
        {
            if (id == _lastKnownVideoId)
                break;

            newIds.Add(id);
        }

        if (newIds.Count == 0)
            return;

        newIds.Reverse();

        foreach (var id in newIds)
        {
            var video = await _youtubeApi.GetVideoByIdAsync(id);

            if (video == null) continue;

            var currentState = video.LiveBroadcastContent;

            _liveStateCache.TryGetValue(video.VideoId, out var previousState);

            _logger.LogInformation(
                "Video: {Title} | Prev: {Prev} -> Now: {Now}",
                video.Title,
                previousState ?? "null",
                currentState
            );

            // 🚫 lần đầu thấy → chỉ lưu
            if (previousState == null)
            {
                _liveStateCache[video.VideoId] = currentState;
                continue;
            }

            // 🔴 chỉ notify khi LIVE START + anti flicker
            if (currentState == "live" && previousState != "live")
            {
                if (_liveCooldown.TryGetValue(video.VideoId, out var lastSent))
                {
                    if ((DateTime.UtcNow - lastSent).TotalMinutes < 10)
                    {
                        _logger.LogInformation("⏳ Skip cooldown: {Title}", video.Title);
                        continue;
                    }
                }

                _logger.LogInformation("🔴 LIVE START: {Title}", video.Title);

                await _discordService.SendVideoNotificationAsync(video);

                _liveCooldown[video.VideoId] = DateTime.UtcNow;
            }

            _liveStateCache[video.VideoId] = currentState;
        }

        // 🔥 save state
        await _liveStateService.SaveAsync(_liveStateCache);

        _lastKnownVideoId = newIds.Last();

        await _persistence.SaveStateAsync(new BotState
        {
            LastVideoId = _lastKnownVideoId
        });
    }
}