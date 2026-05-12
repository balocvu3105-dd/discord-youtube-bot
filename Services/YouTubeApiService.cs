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
        HttpClient httpClient,
        IOptions<BotConfiguration> config,
        ILogger<YouTubeApiService> logger)
    {
        _config = config.Value;

        _logger = logger;

        // =====================================================
        // DEBUG CURRENT API KEY
        // =====================================================

        if (string.IsNullOrWhiteSpace(
                _config.YoutubeApiKey))
        {
            _logger.LogError(
                "❌ YouTube API Key is EMPTY");
        }
        else
        {
            var preview =
                _config.YoutubeApiKey.Length >= 10
                    ? _config.YoutubeApiKey[..10]
                    : _config.YoutubeApiKey;

            _logger.LogInformation(
                "🔑 Current API Key Prefix: {Key}",
                preview);
        }

        // =====================================================
        // YOUTUBE CLIENT
        // =====================================================

        _ytClient = new YouTubeService(
            new BaseClientService.Initializer
            {
                ApiKey =
                    _config.YoutubeApiKey,

                ApplicationName =
                    "YouTubeDiscordBot"
            });

        _ytClient.HttpClient.Timeout =
            TimeSpan.FromSeconds(60);

        _logger.LogInformation(
            "✅ YouTubeApiService initialized");
    }

    // =====================================================
    // GET LATEST VIDEO IDS
    // =====================================================

    public async Task<List<string>>
        GetLatestVideoIdsFromApiAsync()
    {
        try
        {
            _logger.LogInformation(
                "📡 Fetching uploads playlist...");

            // =================================================
            // STEP 1
            // GET CHANNEL CONTENT DETAILS
            // =================================================

            var channelRequest =
                _ytClient.Channels.List(
                    "contentDetails");

            channelRequest.Id =
                _config.YoutubeChannelId;

            var channelResponse =
                await channelRequest.ExecuteAsync();

            if (channelResponse.Items == null ||
                channelResponse.Items.Count == 0)
            {
                _logger.LogWarning(
                    "⚠️ Channel not found");

                return new List<string>();
            }

            var uploadsPlaylistId =
                channelResponse.Items[0]
                    .ContentDetails
                    .RelatedPlaylists
                    .Uploads;

            _logger.LogInformation(
                "📂 Upload playlist ID: {PlaylistId}",
                uploadsPlaylistId);

            // =================================================
            // STEP 2
            // GET LATEST VIDEOS
            // =================================================

            var playlistRequest =
                _ytClient.PlaylistItems.List(
                    "snippet");

            playlistRequest.PlaylistId =
                uploadsPlaylistId;

            playlistRequest.MaxResults = 5;

            var playlistResponse =
                await playlistRequest.ExecuteAsync();

            if (playlistResponse.Items == null)
            {
                _logger.LogWarning(
                    "⚠️ Playlist returned NULL");

                return new List<string>();
            }

            var videoIds =
                playlistResponse.Items
                    .Select(x =>
                        x.Snippet
                            .ResourceId
                            .VideoId)
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

            _logger.LogInformation(
                "🎥 Found {Count} latest videos",
                videoIds.Count);

            foreach (var id in videoIds)
            {
                _logger.LogInformation(
                    "📺 Video ID: {VideoId}",
                    id);
            }

            return videoIds;
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogError(
                ex,
                """
                ❌ YouTube API Error

                Message:
                {Message}

                Error:
                {Error}
                """,
                ex.Message,
                ex.Error?.Message);

            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed to fetch latest videos");

            return new List<string>();
        }
    }

    // =====================================================
    // GET VIDEO DETAIL
    // =====================================================

    public async Task<VideoInfo?>
        GetVideoByIdAsync(
            string videoId)
    {
        for (int attempt = 1;
             attempt <= MaxRetry;
             attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "🎬 Fetching video detail: {VideoId}",
                    videoId);

                var request =
                    _ytClient.Videos.List(
                        "snippet");

                request.Id = videoId;

                var response =
                    await request.ExecuteAsync();

                if (response.Items == null ||
                    response.Items.Count == 0)
                {
                    _logger.LogWarning(
                        "⚠️ Video not found: {VideoId}",
                        videoId);

                    return null;
                }

                var v = response.Items[0];

                var s = v.Snippet;

                var result = new VideoInfo
                {
                    VideoId =
                        v.Id,

                    Title =
                        s.Title,

                    ThumbnailUrl =
                        s.Thumbnails?.High?.Url ?? "",

                    Url =
                        $"https://www.youtube.com/watch?v={v.Id}",

                    ChannelName =
                        s.ChannelTitle,

                    LiveBroadcastContent =
                        s.LiveBroadcastContent
                };

                _logger.LogInformation(
                    """
                    ✅ Video fetched

                    Title: {Title}
                    Live: {Live}
                    """,
                    result.Title,
                    result.LiveBroadcastContent);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    """
                    ⚠️ GetVideoById failed

                    Attempt: {Attempt}
                    VideoId: {VideoId}
                    """,
                    attempt,
                    videoId);

                if (attempt == MaxRetry)
                {
                    _logger.LogError(
                        """
                        ❌ Failed after retries

                        VideoId: {VideoId}
                        """,
                        videoId);

                    return null;
                }

                await DelayWithBackoff(
                    attempt);
            }
        }

        return null;
    }

    // =====================================================
    // HELPER
    // =====================================================

    private async Task DelayWithBackoff(
        int attempt)
    {
        int delaySeconds =
            2 * attempt;

        _logger.LogInformation(
            "⏳ Retry after {Delay}s...",
            delaySeconds);

        await Task.Delay(
            TimeSpan.FromSeconds(delaySeconds));
    }
}