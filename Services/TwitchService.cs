using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class TwitchService : IStreamPlatformProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<TwitchService> _logger;

    // Twitch public frontend GraphQL client ID
    private const string TwitchGqlClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const string TwitchGqlUrl = "https://gql.twitch.tv/gql";

    public string PlatformName => "Twitch";
    public string PlatformEmoji => "🟣";
    public string PlatformColorHex => "9146FF"; // Twitch Purple

    public TwitchService(IHttpClientFactory httpClientFactory, ILogger<TwitchService> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(TwitchService));
        _logger = logger;
    }

    public async Task<StreamStatusResult> CheckLiveStatusAsync(string usernameOrId, CancellationToken ct = default)
    {
        var username = usernameOrId.Trim().ToLowerInvariant();
        try
        {
            var query = new
            {
                query = @"
                    query GetUserStream($login: String!) {
                        user(login: $login) {
                            stream {
                                id
                                title
                                type
                                viewersCount
                                createdAt
                                game {
                                    name
                                }
                            }
                        }
                    }",
                variables = new { login = username }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, TwitchGqlUrl);
            request.Headers.Add("Client-Id", TwitchGqlClientId);
            request.Content = JsonContent.Create(query);

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<TwitchGqlResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Errors is not null && result.Errors.Count > 0)
            {
                var errorMsg = string.Join("; ", result.Errors.Select(e => e.Message));
                throw new InvalidOperationException($"Twitch GraphQL trả về lỗi: {errorMsg}");
            }

            var stream = result?.Data?.User?.Stream;
            if (stream is null || !string.Equals(stream.Type, "live", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[Twitch] @{Username} is OFFLINE", username);
                return StreamStatusResult.Offline(PlatformName, username);
            }

            _logger.LogInformation("[Twitch] @{Username} is LIVE: {Title} ({Viewers} viewers)",
                username, stream.Title, stream.ViewersCount);

            var gameName = stream.Game?.Name ?? "General";
            var displayTitle = !string.IsNullOrWhiteSpace(stream.Title)
                ? $"[{gameName}] {stream.Title}"
                : $"🔴 Đang livestream {gameName}";

            DateTime? startedAt = DateTime.TryParse(stream.CreatedAt, out var dt) ? dt : null;

            return new StreamStatusResult
            {
                IsLive = true,
                Platform = PlatformName,
                UsernameOrId = username,
                Title = displayTitle,
                StreamUrl = $"https://www.twitch.tv/{username}",
                ThumbnailUrl = $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{username}-1280x720.jpg",
                StreamId = stream.Id ?? DateTime.UtcNow.Ticks.ToString(),
                ViewerCount = stream.ViewersCount,
                StartedAtUtc = startedAt
            };
        }
        catch (Exception ex)
        {
            // Để exception ném lên cho Unified Coordinator bắt & backoff mà KHÔNG reset trạng thái
            _logger.LogWarning(ex, "[Twitch] CheckLiveStatusAsync thất bại cho @{Username}", username);
            throw;
        }
    }

    // ── Internal JSON Models ──────────────────────────────────────────────────

    private class TwitchGqlResponse
    {
        [JsonPropertyName("data")]
        public TwitchGqlData? Data { get; set; }

        [JsonPropertyName("errors")]
        public List<TwitchGqlError>? Errors { get; set; }
    }

    private class TwitchGqlError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private class TwitchGqlData
    {
        [JsonPropertyName("user")]
        public TwitchUser? User { get; set; }
    }

    private class TwitchUser
    {
        [JsonPropertyName("stream")]
        public TwitchStream? Stream { get; set; }
    }

    private class TwitchStream
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("viewersCount")]
        public int ViewersCount { get; set; }

        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("game")]
        public TwitchGame? Game { get; set; }
    }

    private class TwitchGame
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
