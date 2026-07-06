using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class FacebookLiveService : IStreamPlatformProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<FacebookLiveService> _logger;

    public string PlatformName => "Facebook";
    public string PlatformEmoji => "🔵";
    public string PlatformColorHex => "1877F2"; // Facebook Blue

    public FacebookLiveService(IHttpClientFactory httpClientFactory, ILogger<FacebookLiveService> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(FacebookLiveService));
        _logger = logger;
    }

    public async Task<StreamStatusResult> CheckLiveStatusAsync(string usernameOrId, CancellationToken ct = default)
    {
        var username = usernameOrId.Trim().TrimEnd('/');
        // Loại bỏ domain nếu user nhập full link
        if (username.Contains("facebook.com/"))
        {
            username = username.Substring(username.IndexOf("facebook.com/") + 13).Trim('/');
        }

        var safeUsername = Uri.EscapeDataString(username);
        var url = $"https://www.facebook.com/{safeUsername}/live/";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[Facebook] Page/profile @{User} không tìm thấy (404)", username);
                return StreamStatusResult.Offline(PlatformName, username);
            }

            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            // Kiểm tra bị Facebook chặn rate limit, yêu cầu đăng nhập hoặc checkpoint
            if (html.Contains("facebook.com/login", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Security Check", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("checkpoint", StringComparison.OrdinalIgnoreCase) ||
                response.RequestMessage?.RequestUri?.ToString().Contains("/login", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new HttpRequestException("Facebook trả về trang đăng nhập/checkpoint - có thể bị rate limit!");
            }

            // Kiểm tra dấu hiệu live streaming trong HTML response của Facebook
            var isLive = html.Contains("\"is_live_streaming\":true", StringComparison.OrdinalIgnoreCase) ||
                         html.Contains("\"broadcast_status\":\"LIVE\"", StringComparison.OrdinalIgnoreCase);

            if (!isLive)
            {
                _logger.LogDebug("[Facebook] @{User} is OFFLINE", username);
                return StreamStatusResult.Offline(PlatformName, username);
            }

            // Thử trích xuất tiêu đề video hoặc video_id nếu có
            var titleMatch = Regex.Match(html, @"\""name\"":\s*\""([^\""]+)\""");
            var title = titleMatch.Success ? Regex.Unescape(titleMatch.Groups[1].Value) : $"🔴 {username} đang phát trực tiếp trên Facebook!";

            var videoIdMatch = Regex.Match(html, @"\""video_id\"":\s*\""(\d+)\""");
            var videoId = videoIdMatch.Success ? videoIdMatch.Groups[1].Value : DateTime.UtcNow.Ticks.ToString();

            _logger.LogInformation("[Facebook] @{User} is LIVE: {Title}", username, title);

            return new StreamStatusResult
            {
                IsLive = true,
                Platform = PlatformName,
                UsernameOrId = username,
                Title = title,
                StreamUrl = url,
                ThumbnailUrl = string.Empty, // Facebook khó cào thumbnail public mà không có token
                StreamId = videoId,
                ViewerCount = 0,
                StartedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Facebook] CheckLiveStatusAsync thất bại cho @{User}", username);
            throw;
        }
    }
}
