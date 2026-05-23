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

    // Các trạng thái "đã xử lý xong" — không cần xử lý lại
    private static readonly HashSet<string> TerminalStatuses = new()
    {
        "video_sent",
        "live_sent",   // FIX: live_sent cũng là terminal — tránh re-notify khi live kết thúc
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

            var currentStatus = liveStates.GetValueOrDefault(videoId, "none");

            // FIX: Skip nếu video đã được xử lý xong (terminal status)
            // Logic cũ: `botState.LastVideoId == videoId && !liveStates.ContainsKey(videoId)`
            // → SAI: video có trong liveStates với "video_sent"/"live_sent" vẫn bị process lại
            if (TerminalStatuses.Contains(currentStatus))
            {
                _logger.LogDebug("Skip {VideoId} — already terminal ({Status})", videoId, currentStatus);
                continue;
            }

            // FIX: Cũng skip nếu là LastVideoId thuần túy (video bình thường đã gửi)
            // nhưng KHÔNG có entry trong liveStates (edge case: state file cũ trước khi có liveStates)
            if (botState.LastVideoId == videoId && currentStatus == "none")
            {
                _logger.LogDebug("Skip {VideoId} — is LastVideoId with no live state", videoId);
                continue;
            }

            var video = await _youtube.GetVideoByIdAsync(videoId);
            if (video is null) continue;

            if (video.LiveBroadcastContent == "live")
            {
                // Chỉ notify 1 lần khi bắt đầu live
                if (!currentStatus.StartsWith("live_notified"))
                {
                    await _discord.SendVideoNotificationAsync(video);
                    liveStates[videoId] = $"live_notified_{DateTime.UtcNow:O}";
                    liveChanged = true;
                    _logger.LogInformation("LIVE notification sent — {VideoId}", videoId);
                }
                else
                {
                    // Đang live, đã notify rồi — cập nhật timestamp để biết live còn active
                    liveStates[videoId] = $"live_notified_{DateTime.UtcNow:O}";
                    liveChanged = true;
                    _logger.LogDebug("Live still active — {VideoId}", videoId);
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
                // Video thường (hoặc live đã kết thúc)
                if (botState.LastVideoId != videoId)
                {
                    await _discord.SendVideoNotificationAsync(video);
                    botState.LastVideoId = videoId;
                    stateChanged = true;
                    liveStates[videoId] = "video_sent";
                    liveChanged = true;
                    _logger.LogInformation("VIDEO notification sent — {VideoId}", videoId);
                    break; // Chỉ xử lý 1 video mới nhất mỗi lần check
                }
                else
                {
                    // FIX: Live đã kết thúc, cập nhật status về terminal để không process lại
                    if (currentStatus != "none" && currentStatus != "video_sent")
                    {
                        liveStates[videoId] = "video_sent";
                        liveChanged = true;
                        _logger.LogInformation("Live ended, marked as video_sent — {VideoId}", videoId);
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