using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class YouTubeApiService
{
    private readonly YouTubeService _ytClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeApiService> _logger;

    private const int MaxRetry = 3;

    public YouTubeApiService(IOptions<BotConfiguration> config, ILogger<YouTubeApiService> logger)
    {
        _config = config.Value;
        _logger = logger;

        _ytClient = new YouTubeService(
            new BaseClientService.Initializer
            {
                ApiKey = _config.YoutubeApiKey,
                ApplicationName = "YouTubeDiscordBot"
            });

        _ytClient.HttpClient.Timeout = TimeSpan.FromSeconds(200);
    }

    // ========================= GET LATEST VIDEOS =========================

    public async Task<List<string>> GetLatestVideoIdsFromApiAsync()
    {
        for (int attempt = 1; attempt <= MaxRetry; attempt++)
        {
            try
            {
                var request = _ytClient.Search.List("snippet");
                request.ChannelId = _config.YoutubeChannelId;
                request.Order = SearchResource.ListRequest.OrderEnum.Date;
                request.MaxResults = 3;
                request.Type = "video";

                var response = await request.ExecuteAsync();

                var ids = response.Items
                    .Select(x => x.Id.VideoId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                _logger.LogInformation("📡 API returned {Count} video IDs (attempt {Attempt})", ids.Count, attempt);

                return ids;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ GetLatest attempt {Attempt} failed", attempt);

                if (attempt == MaxRetry)
                {
                    _logger.LogError("❌ All retry attempts failed for GetLatestVideoIds");
                    return new List<string>();
                }

                await DelayWithBackoff(attempt);
            }
        }

        return new List<string>();
    }

    // ========================= GET CURRENT LIVE =========================

    // Query thẳng eventType=live → YouTube trả về ngay nếu kênh đang live
    // Không phụ thuộc feed video mới → fix được bug live thẳng không notify
    // Tốn 100 units/lần — chạy cùng interval với GetLatestVideoIds
    public async Task<VideoInfo?> GetCurrentLiveAsync()
    {
        for (int attempt = 1; attempt <= MaxRetry; attempt++)
        {
            try
            {
                var request = _ytClient.Search.List("snippet");
                request.ChannelId = _config.YoutubeChannelId;
                request.EventType = SearchResource.ListRequest.EventTypeEnum.Live;
                request.Type = "video";
                request.MaxResults = 1;

                var response = await request.ExecuteAsync();

                var item = response.Items?.FirstOrDefault();

                // Không có item = kênh không đang live
                if (item == null) return null;

                // Lấy full detail (title, thumbnail, url...)
                return await GetVideoByIdAsync(item.Id.VideoId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ GetCurrentLive attempt {Attempt} failed", attempt);

                if (attempt == MaxRetry)
                {
                    _logger.LogError("❌ GetCurrentLiveAsync failed after retries");
                    return null;
                }

                await DelayWithBackoff(attempt);
            }
        }

        return null;
    }

    // ========================= GET VIDEO DETAIL =========================

    public async Task<VideoInfo?> GetVideoByIdAsync(string videoId)
    {
        for (int attempt = 1; attempt <= MaxRetry; attempt++)
        {
            try
            {
                var request = _ytClient.Videos.List("snippet");
                request.Id = videoId;

                var response = await request.ExecuteAsync();

                if (response.Items == null || response.Items.Count == 0)
                    return null;

                var v = response.Items[0];
                var s = v.Snippet;

                return new VideoInfo
                {
                    VideoId = v.Id,
                    Title = s.Title,
                    ThumbnailUrl = s.Thumbnails?.High?.Url ?? "",
                    Url = $"https://www.youtube.com/watch?v={v.Id}",
                    ChannelName = s.ChannelTitle,
                    LiveBroadcastContent = s.LiveBroadcastContent
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ GetVideo attempt {Attempt} failed: {VideoId}", attempt, videoId);

                if (attempt == MaxRetry)
                {
                    _logger.LogError("❌ Failed to fetch video after retries: {VideoId}", videoId);
                    return null;
                }

                await DelayWithBackoff(attempt);
            }
        }

        return null;
    }

    // ========================= HELPER =========================

    private async Task DelayWithBackoff(int attempt)
    {
        int delaySeconds = 2 * attempt;
        _logger.LogInformation("⏳ Retry after {Delay}s...", delaySeconds);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
    }
}