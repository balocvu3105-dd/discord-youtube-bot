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
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeCheckerBackgroundService> _logger;

    // Biến tạm để giữ ID video mới nhất trong bộ nhớ
    private string _lastKnownVideoId = string.Empty;

    public YouTubeCheckerBackgroundService(
        YouTubeApiService youtubeApi,
        DiscordService discordService,
        PersistenceService persistence,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeCheckerBackgroundService> logger)
    {
        _youtubeApi = youtubeApi;
        _discordService = discordService;
        _persistence = persistence;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 YouTube Checker Background Service đang khởi động...");

        // 1. Nạp trạng thái từ file JSON khi bot vừa bật lên
        var savedState = await _persistence.LoadStateAsync();
        _lastKnownVideoId = savedState.LastVideoId ?? string.Empty;

        _logger.LogInformation("Lịch sử video cuối cùng: {Id}",
            string.IsNullOrEmpty(_lastKnownVideoId) ? "Trống" : _lastKnownVideoId);

        // 2. Vòng lặp chính
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForNewVideoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình kiểm tra video.");
            }

            // Nghỉ một khoảng thời gian trước khi check tiếp (lấy từ config)
            int delaySeconds = _config.CheckIntervalSeconds > 0 ? _config.CheckIntervalSeconds : 600;
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        _logger.LogInformation("Stopping YouTube Checker Service...");
    }

    private async Task CheckForNewVideoAsync()
    {
        _logger.LogDebug("--- Đang quét YouTube tại {Time} ---", DateTime.Now);

        VideoInfo? latestVideo = await _youtubeApi.GetLatestVideoAsync();

        if (latestVideo == null || string.IsNullOrEmpty(latestVideo.VideoId))
        {
            return;
        }

        // TRƯỜNG HỢP 1: Lần đầu tiên chạy bot (Chưa có lịch sử)
        if (string.IsNullOrEmpty(_lastKnownVideoId))
        {
            _logger.LogInformation("Phát hiện video đầu tiên: {VideoId}. Đang lưu trạng thái...", latestVideo.VideoId);

            // Bạn có thể chọn gửi thông báo ngay hoặc chỉ lưu lại để chờ video tiếp theo
            await _discordService.SendVideoNotificationAsync(latestVideo);

            _lastKnownVideoId = latestVideo.VideoId;
            await _persistence.SaveStateAsync(new BotState { LastVideoId = _lastKnownVideoId });
            return;
        }

        // TRƯỜNG HỢP 2: Video trùng khớp (Không có gì mới)
        if (latestVideo.VideoId == _lastKnownVideoId)
        {
            _logger.LogDebug("Không có video mới.");
            return;
        }

        // TRƯỜNG HỢP 3: CÓ VIDEO MỚI THỰC SỰ
        _logger.LogInformation("🎉 VIDEO MỚI: {Title}", latestVideo.Title);

        // 1. Gửi thông báo Discord
        await _discordService.SendVideoNotificationAsync(latestVideo);

        // 2. Cập nhật biến tạm
        _lastKnownVideoId = latestVideo.VideoId;

        // 3. Ghi đè vào file JSON ngay lập tức để không bị đăng trùng nếu bot restart
        await _persistence.SaveStateAsync(new BotState { LastVideoId = _lastKnownVideoId });

        _logger.LogInformation("Đã cập nhật trạng thái mới nhất cho video: {Id}", _lastKnownVideoId);
    }
}