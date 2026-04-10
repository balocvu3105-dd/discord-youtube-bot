using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class YouTubeService
{
    private readonly Google.Apis.YouTube.v3.YouTubeService _ytClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeService> _logger;

    public YouTubeService(IOptions<BotConfiguration> config, ILogger<YouTubeService> logger)
    {
        _config = config.Value;
        _logger = logger;

        _ytClient = new Google.Apis.YouTube.v3.YouTubeService(
            new BaseClientService.Initializer
            {
                ApiKey = _config.YouTubeApiKey,
                ApplicationName = "YouTubeDiscordBot"
            });
    }

    public async Task<VideoInfo?> GetLatestVideoAsync()
    {
        try
        {
            var searchRequest = _ytClient.Search.List("snippet");
            searchRequest.ChannelId = _config.YouTubeChannelId;
            searchRequest.Order = SearchResource.ListRequest.OrderEnum.Date;
            searchRequest.MaxResults = 1;
            searchRequest.Type = "video";

            var searchResponse = await searchRequest.ExecuteAsync();

            if (searchResponse.Items == null || searchResponse.Items.Count == 0)
            {
                _logger.LogWarning("Không tìm thấy video nào cho kênh: {ChannelId}", _config.YouTubeChannelId);
                return null;
            }

            var videoId = searchResponse.Items[0].Id.VideoId;

            var videoRequest = _ytClient.Videos.List("snippet,liveStreamingDetails");
            videoRequest.Id = videoId;

            var videoResponse = await videoRequest.ExecuteAsync();

            if (videoResponse.Items == null || videoResponse.Items.Count == 0)
            {
                _logger.LogWarning("Không lấy được chi tiết video ID: {VideoId}", videoId);
                return null;
            }

            var snippet = videoResponse.Items[0].Snippet;

            return new VideoInfo
            {
                VideoId = videoId,
                Title = snippet.Title,
                ChannelName = snippet.ChannelTitle,
                ThumbnailUrl = snippet.Thumbnails?.High?.Url
                                       ?? snippet.Thumbnails?.Medium?.Url
                                       ?? string.Empty,
                LiveBroadcastContent = snippet.LiveBroadcastContent ?? "none"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gọi YouTube API");
            return null;
        }
    }
}