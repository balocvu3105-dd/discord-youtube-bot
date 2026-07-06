using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class KickService : IStreamPlatformProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<KickService> _logger;

    public string PlatformName => "Kick";
    public string PlatformEmoji => "🟢";
    public string PlatformColorHex => "53FC18"; // Kick Green

    public KickService(IHttpClientFactory httpClientFactory, ILogger<KickService> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(KickService));
        _logger = logger;
    }

    public async Task<StreamStatusResult> CheckLiveStatusAsync(string usernameOrId, CancellationToken ct = default)
    {
        var slug = usernameOrId.Trim().ToLowerInvariant();
        var safeSlug = Uri.EscapeDataString(slug);
        var url = $"https://kick.com/api/v2/channels/{safeSlug}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[Kick] Kênh @{Slug} không tìm thấy (404)", slug);
                return StreamStatusResult.Offline(PlatformName, slug);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var channel = JsonSerializer.Deserialize<KickChannelResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var live = channel?.Livestream;
            if (live is null || !live.IsLive)
            {
                _logger.LogDebug("[Kick] @{Slug} is OFFLINE", slug);
                return StreamStatusResult.Offline(PlatformName, slug);
            }

            _logger.LogInformation("[Kick] @{Slug} is LIVE: {Title} ({Viewers} viewers)",
                slug, live.SessionTitle, live.Viewers);

            DateTime? startedAt = DateTime.TryParse(live.CreatedAt, out var dt) ? dt : null;

            return new StreamStatusResult
            {
                IsLive = true,
                Platform = PlatformName,
                UsernameOrId = slug,
                Title = !string.IsNullOrWhiteSpace(live.SessionTitle) ? live.SessionTitle : $"🔴 {slug} đang livestream trên Kick!",
                StreamUrl = $"https://kick.com/{safeSlug}",
                ThumbnailUrl = live.Thumbnail?.Url ?? string.Empty,
                StreamId = live.Id.ToString(),
                ViewerCount = live.Viewers,
                StartedAtUtc = startedAt
            };
        }
        catch (Exception ex)
        {
            // Ném lỗi lên cho Unified Coordinator xử lý để không bị reset trạng thái khi Cloudflare/mạng lỗi
            _logger.LogWarning(ex, "[Kick] CheckLiveStatusAsync thất bại cho @{Slug}", slug);
            throw;
        }
    }

    // ── Internal JSON Models ──────────────────────────────────────────────────

    private class KickChannelResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("livestream")]
        public KickLivestream? Livestream { get; set; }
    }

    private class KickLivestream
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("session_title")]
        public string? SessionTitle { get; set; }

        [JsonPropertyName("is_live")]
        public bool IsLive { get; set; }

        [JsonPropertyName("viewers")]
        public int Viewers { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("thumbnail")]
        public KickThumbnail? Thumbnail { get; set; }
    }

    private class KickThumbnail
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
