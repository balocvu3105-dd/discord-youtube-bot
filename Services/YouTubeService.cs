using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

// Đổi tên thành YouTubeApiService để dứt điểm lỗi CS0101
public class YouTubeApiService
{
    // Chỉ định rõ namespace của Google để không bị nhầm lẫn với class hiện tại
    private readonly Google.Apis.YouTube.v3.YouTubeService _ytClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<YouTubeApiService> _logger;

    public YouTubeApiService(IOptions<BotConfiguration> config, ILogger<YouTubeApiService> logger)
    {
        _config = config.Value;
        _logger = logger;

        _ytClient = new Google.Apis.YouTube.v3.YouTubeService(
            new BaseClientService.Initializer
            {
                ApiKey = _config.YoutubeApiKey,
                ApplicationName = "YouTubeDiscordBot"
            });
    }

    public async Task<VideoInfo?> GetLatestVideoAsync()
    {
        try
        {
            _logger.LogDebug("Đang quét video mới nhất từ YouTube...");

            // Sử dụng Playlist Uploads thay vì Search để tiết kiệm Quota (Chi phí API)
            // Search tốn 100 units, PlaylistItems chỉ tốn 1 unit.
            string uploadsPlaylistId = "UU" + _config.YoutubeChannelId.Substring(2);

            var playlistRequest = _ytClient.PlaylistItems.List("snippet");
            playlistRequest.PlaylistId = uploadsPlaylistId;
            playlistRequest.MaxResults = 1;

            var response = await playlistRequest.ExecuteAsync();

            if (response.Items == null || response.Items.Count == 0)
            {
                _logger.LogWarning("Không tìm thấy video nào cho kênh: {ChannelId}", _config.YoutubeChannelId);
                return null;
            }

            var item = response.Items[0];
            var snippet = item.Snippet;

            return new VideoInfo
            {
                VideoId = snippet.ResourceId.VideoId,
                Title = snippet.Title,
                // Thumbnail ưu tiên bản High
                ThumbnailUrl = snippet.Thumbnails?.High?.Url
                               ?? snippet.Thumbnails?.Medium?.Url
                               ?? string.Empty,
                // Link video chuẩn
                Url = $"https://www.youtube.com/watch?v={snippet.ResourceId.VideoId}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi nghiêm trọng khi gọi YouTube API");
            return null;
        }
    }
}