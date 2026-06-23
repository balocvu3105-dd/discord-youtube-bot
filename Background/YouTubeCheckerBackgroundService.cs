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
    private readonly IYouTubeApiService _youtube;
    private readonly IPersistenceService _persistence;
    private readonly ILiveStateService _liveState;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeCheckerBackgroundService> _logger;

    // ✅ FIX: Giữ state trong memory — chỉ load từ disk 1 lần khi startup.
    // Trước đây CheckYouTubeAsync load lại từ disk mỗi lần check, dẫn đến
    // state bị reset về giá trị cũ sau mỗi 2 phút → duplicate notifications.
    private BotState _botState = new();
    private Dictionary<string, string> _liveStates = new();

    private static readonly HashSet<string> TerminalStatuses = new()
    {
        "video_sent",
        "live_sent",
    };

    public YouTubeCheckerBackgroundService(
        IDiscordService discord,
        IYouTubeApiService youtube,
        IPersistenceService persistence,
        ILiveStateService liveState,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeCheckerBackgroundService> logger)
    {
        _discord = discord;
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

        // FIX: dùng IDiscordService.WaitForReadyAsync — không cần inject concrete DiscordService
        await _discord.WaitForReadyAsync();
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

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Khi bot restart: lấy 5 video mới nhất từ mỗi channel YouTube,
    /// đánh dấu tất cả là "video_sent" nếu chưa có status,
    /// trừ video nào đang live thật sự.
    /// </summary>
    private async Task SyncStateOnStartupAsync()
    {
        try
        {
            // ✅ Load vào fields — đây là lần load DUY NHẤT từ disk
            _botState = await _persistence.LoadStateAsync();
            _liveStates = await _liveState.LoadAsync();

            // Migrate legacy LastVideoId (single channel) → LastVideoIds (multi-channel)
            if (!string.IsNullOrEmpty(_botState.LastVideoId) && _botState.LastVideoIds.Count == 0)
            {
                var firstChannel = _config.YoutubeChannelIds.FirstOrDefault();
                if (firstChannel != null)
                {
                    _botState.LastVideoIds[firstChannel] = _botState.LastVideoId;
                    _logger.LogInformation(
                        "Migrated legacy LastVideoId={VideoId} → LastVideoIds[{Channel}]",
                        _botState.LastVideoId, firstChannel);
                }
                _botState.LastVideoId = string.Empty;
            }

            var changed = false;
            var stateChanged = false;

            foreach (var channelId in _config.YoutubeChannelIds)
            {
                if (string.IsNullOrWhiteSpace(channelId)) continue;

                var lastVideoId = _botState.LastVideoIds.GetValueOrDefault(channelId, string.Empty);
                var videoIds = await _youtube.GetLatestVideoIdsAsync(channelId);

                // Tìm vị trí của lastVideoId trong danh sách (newest-first).
                // -1 = lastVideoId cũ hơn tất cả top-5 (hoặc chưa có lastVideoId).
                var hasReference = !string.IsNullOrEmpty(lastVideoId);
                var lastKnownIndex = hasReference ? videoIds.IndexOf(lastVideoId) : -1;

                // Chỉ gửi 1 video mới nhất mỗi channel khi startup
                var sentNewOnStartup = false;

                for (var i = 0; i < videoIds.Count; i++)
                {
                    var videoId = videoIds[i];
                    var currentStatus = _liveStates.GetValueOrDefault(videoId, "none");

                    // Đã có status rồi → không cần sync
                    if (currentStatus != "none") continue;

                    // Là LastVideoId đã biết → đánh terminal
                    if (lastVideoId == videoId)
                    {
                        _liveStates[videoId] = "video_sent";
                        changed = true;
                        _logger.LogInformation(
                            "Startup sync [{Channel}]: marked LastVideoId as video_sent — {VideoId}", channelId, videoId);
                        continue;
                    }

                    // Video chưa biết → fetch để kiểm tra
                    var video = await _youtube.GetVideoByIdAsync(videoId);
                    if (video is null) continue;

                    if (video.LiveBroadcastContent == "live")
                    {
                        // Đang live thật → gửi thông báo live
                        await _discord.SendVideoNotificationAsync(video);
                        _liveStates[videoId] = "live_notified";
                        changed = true;
                        _logger.LogInformation(
                            "Startup sync [{Channel}]: active live — sent notification & marked live_notified — {VideoId}", channelId, videoId);
                    }
                    else if (video.LiveBroadcastContent == "none"
                             && hasReference
                             && (lastKnownIndex == -1 || i < lastKnownIndex)
                             && !sentNewOnStartup)
                    {
                        // Video MỚI hơn lastVideoId, upload trong lúc bot offline → thông báo
                        await _discord.SendVideoNotificationAsync(video);
                        _botState.LastVideoIds[channelId] = videoId;
                        _liveStates[videoId] = "video_sent";
                        changed = true;
                        stateChanged = true;
                        sentNewOnStartup = true;
                        _logger.LogInformation(
                            "Startup sync [{Channel}]: new video uploaded while offline — sent notification — {VideoId}", channelId, videoId);
                    }
                    else
                    {
                        // Video cũ hoặc upcoming → đánh terminal, không thông báo
                        _liveStates[videoId] = "video_sent";
                        changed = true;
                        _logger.LogInformation(
                            "Startup sync [{Channel}]: marked old video as video_sent — {VideoId}", channelId, videoId);
                    }
                }
            }

            if (changed)
                await _liveState.SaveAsync(_liveStates);

            // Lưu state (migrate hoặc update lastVideoId trên startup)
            await _persistence.SaveStateAsync(_botState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncStateOnStartupAsync thất bại — bỏ qua, tiếp tục chạy");
        }
    }

    private async Task CheckYouTubeAsync(CancellationToken ct)
    {
        // ✅ FIX: Dùng _botState / _liveStates từ memory — KHÔNG load lại từ disk.
        // Load lại từ disk mỗi tick là root cause của duplicate notifications.
        var stateChanged = false;
        var liveChanged = false;

        foreach (var channelId in _config.YoutubeChannelIds)
        {
            if (string.IsNullOrWhiteSpace(channelId) || ct.IsCancellationRequested) break;

            var videoIds = await _youtube.GetLatestVideoIdsAsync(channelId);
            if (videoIds.Count == 0) continue;

            var lastVideoId = _botState.LastVideoIds.GetValueOrDefault(channelId, string.Empty);

            foreach (var videoId in videoIds)
            {
                if (ct.IsCancellationRequested) break;

                var currentStatus = _liveStates.GetValueOrDefault(videoId, "none");

                // Skip terminal
                if (TerminalStatuses.Contains(currentStatus))
                {
                    _logger.LogDebug("Skip {VideoId} — terminal ({Status})", videoId, currentStatus);
                    continue;
                }

                // Skip LastVideoId không có live state (video cũ trước khi có liveStates)
                if (lastVideoId == videoId && currentStatus == "none")
                {
                    _liveStates[videoId] = "video_sent";
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
                        _liveStates[videoId] = "live_notified";
                        liveChanged = true;
                        _logger.LogInformation("LIVE notification sent [{Channel}] — {VideoId}", channelId, videoId);
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
                        _liveStates[videoId] = "upcoming";
                        liveChanged = true;
                        _logger.LogInformation("Upcoming detected [{Channel}] — {VideoId}", channelId, videoId);
                    }
                }
                else
                {
                    // LiveBroadcastContent = "none":
                    // Có thể là (A) video upload thường mới, hoặc (B) livestream vừa kết thúc.
                    var wasLive = currentStatus.StartsWith("live_notified")
                               || currentStatus == "upcoming";

                    if (wasLive)
                    {
                        // Livestream vừa kết thúc → đánh terminal, KHÔNG gửi "video mới"
                        _liveStates[videoId] = "video_sent";
                        liveChanged = true;
                        _logger.LogInformation(
                            "Live ended (was {Status}) → video_sent, no re-notify [{Channel}] — {VideoId}",
                            currentStatus, channelId, videoId);
                    }
                    else if (lastVideoId != videoId)
                    {
                        // Video upload thường thật sự mới → gửi thông báo
                        await _discord.SendVideoNotificationAsync(video);
                        _botState.LastVideoIds[channelId] = videoId;
                        stateChanged = true;
                        _liveStates[videoId] = "video_sent";
                        liveChanged = true;
                        _logger.LogInformation("VIDEO notification sent [{Channel}] — {VideoId}", channelId, videoId);
                        break; // Chỉ 1 video mới nhất mỗi channel mỗi lần check
                    }
                    else
                    {
                        // Video đã biết, không phải live, không cần làm gì
                        _logger.LogDebug("Skip known non-live video — {VideoId}", videoId);
                    }
                }
            }
        }

        if (stateChanged)
            await _persistence.SaveStateAsync(_botState);

        if (liveChanged)
            await _liveState.SaveAsync(_liveStates);
    }
}