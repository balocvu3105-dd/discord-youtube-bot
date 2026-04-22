using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Xml.Linq;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class YouTubeApiService
{
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

    // 🔥 RSS: detect nhanh video ID
    public async Task<List<string>> GetLatestVideoIdsFromRssAsync()
    {
        try
        {
            var url = $"https://www.youtube.com/feeds/videos.xml?channel_id={_config.YoutubeChannelId}";

            using var http = new HttpClient();
            var xml = await http.GetStringAsync(url);

            var doc = XDocument.Parse(xml);

            var ids = doc.Descendants()
                .Where(x => x.Name.LocalName == "videoId")
                .Select(x => x.Value)
                .Take(3)
                .ToList();

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi RSS");
            return new List<string>();
        }
    }

    // 🔥 API: lấy detail + live status
    public async Task<VideoInfo?> GetVideoByIdAsync(string videoId)
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
            _logger.LogError(ex, "Lỗi khi gọi Videos API");
            return null;
        }
    }
}