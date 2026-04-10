using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class YouTubeCheckerBackgroundService : BackgroundService
{
    private readonly YouTubeService _youTubeService;
    private readonly DiscordService _discordService;
    private readonly PersistenceService _persistenceService;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeCheckerBackgroundService> _logger;

    private string _lastKnownVideoId = string.Empty;

    public YouTubeCheckerBackgroundService(
        YouTubeService youTubeService,
        DiscordService discordService,
        PersistenceService persistenceService,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeCheckerBackgroundService> logger)
    {
        _youTubeService = youTubeService;
        _discordService = discordService;
        _persistenceService = persistenceService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("YouTube Checker đang khởi động...");

        var savedState = await _persistenceService.LoadStateAsync();
        _lastKnownVideoId = savedState.LastVideoId ?? string.Empty;

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckForNewVideoAsync();

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_config.CheckIntervalSeconds),
                    stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("YouTube Checker đã dừng.");
    }

    private async Task CheckForNewVideoAsync()
    {
        _logger.LogDebug("Đang kiểm tra video mới...");

        VideoInfo? latestVideo = await _youTubeService.GetLatestVideoAsync();
        if (latestVideo == null) return;

        // 🔥 QUAN TRỌNG CHO RENDER FREE
        // Lần đầu chạy (mỗi lần app thức dậy) vẫn gửi thông báo
        if (string.IsNullOrEmpty(_lastKnownVideoId))
        {
            _logger.LogInformation(
                "Lần đầu chạy trên Render — VẪN gửi thông báo cho video: {VideoId}",
                latestVideo.VideoId);

            await _discordService.SendVideoNotificationAsync(latestVideo);

            _lastKnownVideoId = latestVideo.VideoId;
            await _persistenceService.SaveStateAsync(
                new BotState { LastVideoId = _lastKnownVideoId });

            return;
        }

        // Không có video mới
        if (latestVideo.VideoId == _lastKnownVideoId)
        {
            _logger.LogDebug("Không có video mới.");
            return;
        }

        // Có video mới
        _logger.LogInformation("🎉 VIDEO/LIVESTREAM MỚI: {Title}", latestVideo.Title);

        await _discordService.SendVideoNotificationAsync(latestVideo);

        _lastKnownVideoId = latestVideo.VideoId;
        await _persistenceService.SaveStateAsync(
            new BotState { LastVideoId = _lastKnownVideoId });
    }
}