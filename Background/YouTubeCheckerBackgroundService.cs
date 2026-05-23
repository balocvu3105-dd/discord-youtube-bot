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

    private static readonly HashSet<string> TerminalStatuses = new()
    {
        "video_sent",
        "live_sent",
    };

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

        // ── FIX: Sync liveStates với LastVideoId khi startup ──────────────
        // Đảm bảo video đã biết (LastVideoId) luôn có status terminal
        // tránh gửi lại khi restart VPS
        await SyncStateOnStartupAsync();

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

    /// <summary>
    /// Khi bot restart: lấy 5 video mới nhất từ YouTube,
    /// đánh dấu tất cả là "video_sent" nếu chưa có status,
    /// trừ video nào đang live thật sự.
    /// </summary>
    private async Task SyncStateOnStartupAsync()
    {
        try
        {
            var botState = await _persistence.LoadStateAsync();
            var liveStates = await _liveState.LoadAsync();
            var changed = false;

            var videoIds = await _youtube.GetLatestVideoIdsAsync();

            foreach (var videoId in videoIds)
            {
                var currentStatus = liveStates.GetValueOrDefault(videoId, "none");

                // Đã có status rồi → không cần sync
                if (currentStatus != "none") continue;

                // Là LastVideoId đã biết → đánh dấu terminal ngay
                if (botState.LastVideoId == videoId)
                {
                    liveStates[videoId] = "video_sent";
                    changed = true;
                    _logger.LogInformation("Startup sync: marked LastVideoId as video_sent — {VideoId}", videoId);
                    continue;
                }

                // Video chưa biết → fetch để kiểm tra có đang live không
                var video = await _youtube.GetVideoByIdAsync(videoId);
                if (video is null) continue;

                if (video.LiveBroadcastContent == "live")
                {
                    // Đang live thật → giữ nguyên để CheckYouTubeAsync xử lý
                    _logger.LogInformation("Startup sync: active live detected — {VideoId}", videoId);
                }
                else
                {
                    // Video cũ hơn LastVideoId hoặc video không biết → đánh dấu terminal
                    liveStates[videoId] = "video_sent";
                    changed = true;
                    _logger.LogInformation("Startup sync: marked old video as video_sent — {VideoId}", videoId);
                }
            }

            if (changed)
                await _liveState.SaveAsync(liveStates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncStateOnStartupAsync thất bại — bỏ qua, tiếp tục chạy");
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

            var currentStatus = liveStates.GetValueOrDefault(videoId, "none");

            // Skip terminal
            if (TerminalStatuses.Contains(currentStatus))
            {
                _logger.LogDebug("Skip {VideoId} — terminal ({Status})", videoId, currentStatus);
                continue;
            }

            // Skip LastVideoId không có live state (video cũ trước khi có liveStates)
            if (botState.LastVideoId == videoId && currentStatus == "none")
            {
                // Đánh dấu luôn để lần sau không check lại
                liveStates[videoId] = "video_sent";
                liveChanged = true;
                _logger.LogDebug("Skip & mark {VideoId} — LastVideoId with no live state", videoId);
                continue;
            }

            var video = await _youtube.GetVideoByIdAsync(videoId);
            if (video is null) continue;

            if (video.LiveBroadcastContent == "live")
            {
                if (!currentStatus.StartsWith("live_notified"))
                {
                    await _discord.SendVideoNotificationAsync(video);
                    // FIX: dùng "live_notified" cố định, không append timestamp
                    // tránh status thay đổi mỗi lần → luôn StartsWith đúng
                    liveStates[videoId] = "live_notified";
                    liveChanged = true;
                    _logger.LogInformation("LIVE notification sent — {VideoId}", videoId);
                }
                else
                {
                    _logger.LogDebug("Live still active, skip — {VideoId}", videoId);
                }
            }
            else if (video.LiveBroadcastContent == "upcoming")
            {
                if (currentStatus == "none")
                {
                    liveStates[videoId] = "upcoming";
                    liveChanged = true;
                    _logger.LogInformation("Upcoming detected — {VideoId}", videoId);
                }
            }
            else
            {
                // Video thường hoặc live đã kết thúc
                if (botState.LastVideoId != videoId)
                {
                    await _discord.SendVideoNotificationAsync(video);
                    botState.LastVideoId = videoId;
                    stateChanged = true;
                    liveStates[videoId] = "video_sent";
                    liveChanged = true;
                    _logger.LogInformation("VIDEO notification sent — {VideoId}", videoId);
                    break; // Chỉ 1 video mới nhất mỗi lần check
                }
                else
                {
                    // Live kết thúc → đánh dấu terminal
                    if (currentStatus != "none" && !TerminalStatuses.Contains(currentStatus))
                    {
                        liveStates[videoId] = "video_sent";
                        liveChanged = true;
                        _logger.LogInformation("Live ended → video_sent — {VideoId}", videoId);
                    }
                }
            }
        }

        if (stateChanged)
            await _persistence.SaveStateAsync(botState);

        if (liveChanged)
            await _liveState.SaveAsync(liveStates);
    }
}