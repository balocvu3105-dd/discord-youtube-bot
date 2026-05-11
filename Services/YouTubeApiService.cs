using System.ServiceModel.Syndication;
using System.Xml;
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

    public YouTubeApiService(
        IOptions<BotConfiguration> config,
        ILogger<YouTubeApiService> logger)
    {
        _config = config.Value;
        _logger = logger;

        _ytClient = new YouTubeService(
            new BaseClientService.Initializer
            {
                ApiKey = _config.YoutubeApiKey,
                ApplicationName = "YouTubeDiscordBot"
            });

        _ytClient.HttpClient.Timeout =
            TimeSpan.FromSeconds(200);
    }

    // =========================================================
    // VIDEO VIA RSS (0 quota)
    // =========================================================

    public async Task<List<string>> GetLatestVideoIdsFromApiAsync()
    {
        try
        {
            var rssUrl =
                $"https://www.youtube.com/feeds/videos.xml?channel_id={_config.YoutubeChannelId}";

            using var reader =
                XmlReader.Create(rssUrl);

            var feed =
                SyndicationFeed.Load(reader);

            var ids = feed.Items
                .Take(3)
                .Select(item =>
                    item.Id.Split(':').Last())
                .ToList();

            _logger.LogInformation(
                "📡 RSS returned {Count} video IDs",
                ids.Count);

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ RSS fetch failed");

            return new List<string>();
        }
    }

    // =========================================================
    // LIVESTREAM VIA API
    // =========================================================

    public async Task<VideoInfo?> GetCurrentLiveAsync()
    {
        for (int attempt = 1; attempt <= MaxRetry; attempt++)
        {
            try
            {
                var request =
                    _ytClient.Search.List("snippet");

                request.ChannelId =
                    _config.YoutubeChannelId;

                request.EventType =
                    SearchResource.ListRequest.EventTypeEnum.Live;

                request.Type =
                    "video";

                request.MaxResults = 1;

                var response =
                    await request.ExecuteAsync();

                var item =
                    response.Items?.FirstOrDefault();

                if (item == null)
                    return null;

                return await GetVideoByIdAsync(
                    item.Id.VideoId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ GetCurrentLive attempt {Attempt} failed",
                    attempt);

                if (attempt == MaxRetry)
                {
                    _logger.LogError(
                        "❌ GetCurrentLiveAsync failed after retries");

                    return null;
                }

                await DelayWithBackoff(attempt);
            }
        }

        return null;
    }

    // =========================================================
    // VIDEO DETAIL
    // =========================================================

    public async Task<VideoInfo?> GetVideoByIdAsync(
        string videoId)
    {
        for (int attempt = 1; attempt <= MaxRetry; attempt++)
        {
            try
            {
                var request =
                    _ytClient.Videos.List("snippet");

                request.Id = videoId;

                var response =
                    await request.ExecuteAsync();

                if (response.Items == null ||
                    response.Items.Count == 0)
                {
                    return null;
                }

                var v = response.Items[0];

                var s = v.Snippet;

                return new VideoInfo
                {
                    VideoId = v.Id,
                    Title = s.Title,
                    ThumbnailUrl =
                        s.Thumbnails?.High?.Url ?? "",

                    Url =
                        $"https://www.youtube.com/watch?v={v.Id}",

                    ChannelName =
                        s.ChannelTitle,

                    LiveBroadcastContent =
                        s.LiveBroadcastContent
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ GetVideo attempt {Attempt} failed: {VideoId}",
                    attempt,
                    videoId);

                if (attempt == MaxRetry)
                {
                    _logger.LogError(
                        "❌ Failed to fetch video after retries: {VideoId}",
                        videoId);

                    return null;
                }

                await DelayWithBackoff(attempt);
            }
        }

        return null;
    }

    // =========================================================
    // HELPER
    // =========================================================

    private async Task DelayWithBackoff(int attempt)
    {
        int delaySeconds = 2 * attempt;

        _logger.LogInformation(
            "⏳ Retry after {Delay}s...",
            delaySeconds);

        await Task.Delay(
            TimeSpan.FromSeconds(delaySeconds));
    }
}

