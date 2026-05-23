using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Giao tiếp với YouTube Data API v3.
///
/// Quota YouTube:
///   10,000 units/ngày. PlaylistItems.list = 1 unit, Videos.list = 1 unit.
///   → Bot check 120s/lần = 720 lần/ngày × 2 API calls = 1440 units — rất an toàn.
///   Nếu gặp 403 quotaExceeded → log warning, trả về empty list, retry lần sau.
/// </summary>
public class YouTubeApiService : IYouTubeApiService
{
    private readonly YouTubeService _ytClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeApiService> _logger;

    public YouTubeApiService(
        IOptions<BotConfiguration> config,
        ILogger<YouTubeApiService> logger)
    {
        _config = config.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_config.YoutubeApiKey))
            _logger.LogError("YouTube API Key bị trống — bot sẽ không poll được YouTube");
        else
        {
            var preview = _config.YoutubeApiKey.Length >= 8
                ? _config.YoutubeApiKey[..8] + "..."
                : "***";
            _logger.LogInformation("YouTube API Key prefix: {Preview}", preview);
        }

        _ytClient = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = _config.YoutubeApiKey,
            ApplicationName = "YouTubeDiscordBot"
        });

        _ytClient.HttpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger.LogInformation("YouTubeApiService initialized");
    }

    public async Task<List<string>> GetLatestVideoIdsAsync()
    {
        try
        {
            var channelReq = _ytClient.Channels.List("contentDetails");
            channelReq.Id = _config.YoutubeChannelId;
            var channelResp = await channelReq.ExecuteAsync();

            if (channelResp.Items is null || channelResp.Items.Count == 0)
            {
                _logger.LogWarning("Channel không tìm thấy: {Id}", _config.YoutubeChannelId);
                return [];
            }

            var playlistId = channelResp.Items[0].ContentDetails.RelatedPlaylists.Uploads;

            var playlistReq = _ytClient.PlaylistItems.List("snippet");
            playlistReq.PlaylistId = playlistId;
            playlistReq.MaxResults = 5;
            var playlistResp = await playlistReq.ExecuteAsync();

            if (playlistResp.Items is null) return [];

            var ids = playlistResp.Items
                .Select(x => x.Snippet.ResourceId.VideoId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            _logger.LogDebug("Fetched {Count} latest video IDs", ids.Count);
            return ids;
        }
        catch (Google.GoogleApiException ex) when (ex.Error?.Code == 403)
        {
            _logger.LogWarning(
                "YouTube quota exceeded (403) — bot sẽ tự retry sau {Seconds}s",
                _config.CheckIntervalSeconds);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetLatestVideoIdsAsync thất bại");
            return [];
        }
    }

    public async Task<VideoInfo?> GetVideoByIdAsync(string videoId)
    {
        try
        {
            var req = _ytClient.Videos.List("snippet");
            req.Id = videoId;
            var resp = await req.ExecuteAsync();

            if (resp.Items is null || resp.Items.Count == 0)
            {
                _logger.LogWarning("Video không tìm thấy: {VideoId}", videoId);
                return null;
            }

            var v = resp.Items[0];
            return new VideoInfo
            {
                VideoId = v.Id,
                Title = v.Snippet.Title,
                ThumbnailUrl = v.Snippet.Thumbnails?.High?.Url ?? string.Empty,
                Url = $"https://www.youtube.com/watch?v={v.Id}",
                ChannelName = v.Snippet.ChannelTitle,
                LiveBroadcastContent = v.Snippet.LiveBroadcastContent ?? "none"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetVideoByIdAsync thất bại — {VideoId}", videoId);
            return null;
        }
    }
}