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
                // ✅ FIX: Bắt TaskCanceledException riêng để tránh crash khi
                // stoppingToken bị cancel (shutdown bình thường hoặc Discord disconnect).
                await Task.Delay(TimeSpan.FromSeconds(_config.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Khi bot restart: lấy 5 video mới nhất từ YouTube,
    /// đánh dấu tất cả là "video_sent" nếu chưa có status,
    /// trừ video nào đang live thật sự.
    ///
    /// FIX: Video đang live → set "live_notified" ngay (thay vì bỏ trống).
    /// Nếu để status = "none" mà live kết thúc trước lần check tiếp theo,
    /// CheckYouTubeAsync sẽ không biết đây là live đã qua → gửi "video mới".
    ///
    /// ✅ FIX: Load vào _botState / _liveStates (fields) thay vì biến local,
    /// để CheckYouTubeAsync dùng lại được mà không cần load lại từ disk.
    /// </summary>
    private async Task SyncStateOnStartupAsync()
    {
        try
        {
            // ✅ Load vào fields — đây là lần load DUY NHẤT từ disk
            _botState = await _persistence.LoadStateAsync();
            _liveStates = await _liveState.LoadAsync();
            var changed = false;

            var videoIds = await _youtube.GetLatestVideoIdsAsync();

            foreach (var videoId in videoIds)
            {
                var currentStatus = _liveStates.GetValueOrDefault(videoId, "none");

                // Đã có status rồi → không cần sync
                if (currentStatus != "none") continue;

                // Là LastVideoId đã biết → đánh dấu terminal ngay
                if (_botState.LastVideoId == videoId)
                {
                    _liveStates[videoId] = "video_sent";
                    changed = true;
                    _logger.LogInformation(
                        "Startup sync: marked LastVideoId as video_sent — {VideoId}", videoId);
                    continue;
                }

                // Video chưa biết → fetch để kiểm tra có đang live không
                var video = await _youtube.GetVideoByIdAsync(videoId);
                if (video is null) continue;

                if (video.LiveBroadcastContent == "live")
                {
                    // FIX: Set "live_notified" ngay thay vì để "none".
                    //
                    // Lý do: Nếu live kết thúc trước khi CheckYouTubeAsync chạy,
                    // status vẫn là "none" → CheckYouTubeAsync thấy video này
                    // có LiveBroadcastContent="none" + LastVideoId != videoId
                    // → tưởng là video upload mới → gửi thêm thông báo sai.
                    //
                    // Với "live_notified", CheckYouTubeAsync sẽ nhận ra đây là
                    // live đã kết thúc (wasLive = true) → chỉ đánh terminal, không gửi.
                    _liveStates[videoId] = "live_notified";
                    changed = true;
                    _logger.LogInformation(
                        "Startup sync: active live detected, marked live_notified — {VideoId}", videoId);
                }
                else
                {
                    // Video cũ hoặc video không biết → đánh dấu terminal
                    _liveStates[videoId] = "video_sent";
                    changed = true;
                    _logger.LogInformation(
                        "Startup sync: marked old video as video_sent — {VideoId}", videoId);
                }
            }

            if (changed)
                await _liveState.SaveAsync(_liveStates);
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

        // ✅ FIX: Dùng _botState / _liveStates từ memory — KHÔNG load lại từ disk.
        // Load lại từ disk mỗi tick là root cause của duplicate notifications:
        //   - Tick 1: load disk (LastVideoId=A) → thấy B mới hơn → gửi B → save (LastVideoId=B)
        //   - Tick 2: load disk lại → file vẫn đang ghi dở hoặc race condition → thấy A "mới hơn" → gửi A
        //   - → Loop vô hạn xen kẽ A/B
        var stateChanged = false;
        var liveChanged = false;

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
            if (_botState.LastVideoId == videoId && currentStatus == "none")
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
                    _liveStates[videoId] = "upcoming";
                    liveChanged = true;
                    _logger.LogInformation("Upcoming detected — {VideoId}", videoId);
                }
            }
            else
            {
                // LiveBroadcastContent = "none":
                // Có thể là (A) video upload thường mới, hoặc (B) livestream vừa kết thúc.
                //
                // FIX: Phải phân biệt 2 trường hợp này dựa vào lịch sử status.
                // Không phân biệt → trường hợp (B) sẽ bị gửi thêm thông báo "video mới" sai.
                //
                // wasLive = true  → đây là livestream vừa kết thúc → chỉ đánh terminal
                // wasLive = false → đây là video upload thường → xử lý bình thường
                var wasLive = currentStatus.StartsWith("live_notified")
                           || currentStatus == "upcoming";

                if (wasLive)
                {
                    // Livestream vừa kết thúc → đánh terminal, KHÔNG gửi "video mới"
                    _liveStates[videoId] = "video_sent";
                    liveChanged = true;
                    _logger.LogInformation(
                        "Live ended (was {Status}) → video_sent, no re-notify — {VideoId}",
                        currentStatus, videoId);
                }
                else if (_botState.LastVideoId != videoId)
                {
                    // Video upload thường thật sự mới → gửi thông báo
                    await _discord.SendVideoNotificationAsync(video);
                    _botState.LastVideoId = videoId;
                    stateChanged = true;
                    _liveStates[videoId] = "video_sent";
                    liveChanged = true;
                    _logger.LogInformation("VIDEO notification sent — {VideoId}", videoId);
                    break; // Chỉ 1 video mới nhất mỗi lần check
                }
                else
                {
                    // Video đã biết, không phải live, không cần làm gì
                    _logger.LogDebug("Skip known non-live video — {VideoId}", videoId);
                }
            }
        }

        if (stateChanged)
            await _persistence.SaveStateAsync(_botState);

        if (liveChanged)
            await _liveState.SaveAsync(_liveStates);
    }
}