using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Kiểm tra TikTok live status bằng cách scrape trang profile.
/// Không cần API key — dùng endpoint webcast public của TikTok.
///
/// Cơ chế:
///   1. Lấy roomId từ trang @username/live (JSON __UNIVERSAL_DATA_FOR_REHYDRATION__)
///   2. Ping webcast endpoint để xác nhận room còn alive
/// </summary>
public partial class TikTokService : ITikTokService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TikTokService> _logger;

    // Regex trích xuất block JSON của __UNIVERSAL_DATA_FOR_REHYDRATION__
    [GeneratedRegex(@"<script[^>]*id=""__UNIVERSAL_DATA_FOR_REHYDRATION__""[^>]*>(.*?)</script>",
        RegexOptions.Singleline)]
    private static partial Regex UniversalDataRegex();

    public TikTokService(HttpClient httpClient, ILogger<TikTokService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> IsLiveAsync(string username)
    {
        try
        {
            // Bước 1: Lấy roomId từ trang /live
            var roomId = await GetRoomIdAsync(username);
            if (string.IsNullOrEmpty(roomId))
            {
                _logger.LogDebug("TikTok @{Username} — không tìm thấy roomId (không live)", username);
                return false;
            }

            // Bước 2: Kiểm tra room còn alive không
            var alive = await CheckRoomAliveAsync(roomId);
            _logger.LogDebug("TikTok @{Username} — roomId={RoomId}, alive={Alive}", username, roomId, alive);
            return alive;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TikTokService.IsLiveAsync thất bại cho @{Username}", username);
            return false;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<string?> GetRoomIdAsync(string username)
    {
        var url = $"https://www.tiktok.com/@{username}/live";

        using var response = await _httpClient.GetAsync(url);

        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "null";
        _logger.LogInformation(
            "TikTok @{Username} — HTTP {Status}, finalUrl={FinalUrl}",
            username, (int)response.StatusCode, finalUrl);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        // TikTok redirect về trang chính khi không live (301/302 về /@username)
        // Nếu URL cuối cùng không còn /live → không live
        if (response.RequestMessage?.RequestUri is { } finalUri
            && !finalUri.PathAndQuery.Contains("/live", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("TikTok @{Username} — redirect ra khỏi /live → không live", username);
            return null;
        }

        var html = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("TikTok @{Username} — HTML {HtmlLength} chars", username, html.Length);

        var match = UniversalDataRegex().Match(html);
        if (!match.Success)
        {
            _logger.LogDebug("TikTok @{Username} — không tìm thấy __UNIVERSAL_DATA_FOR_REHYDRATION__", username);
            return null;
        }

        var json = match.Groups[1].Value;
        using var doc = JsonDocument.Parse(json);

        // Duyệt qua tất cả properties để tìm roomId
        return FindRoomId(doc.RootElement);
    }

    /// <summary>
    /// Tìm đệ quy "roomId" trong JSON object.
    /// TikTok thay đổi cấu trúc JSON khá thường xuyên nên dùng cách này để robust hơn.
    /// </summary>
    private static string? FindRoomId(JsonElement element, int depth = 0)
    {
        if (depth > 10) return null; // tránh đệ quy sâu vô hạn

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if ((prop.Name == "roomId" || prop.Name == "room_id")
                    && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(val) && val != "0")
                        return val;
                }

                var found = FindRoomId(prop.Value, depth + 1);
                if (found != null) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindRoomId(item, depth + 1);
                if (found != null) return found;
            }
        }

        return null;
    }

    private async Task<bool> CheckRoomAliveAsync(string roomId)
    {
        var url = $"https://webcast.tiktok.com/webcast/room/check_alive/?aid=1988&room_ids={roomId}";

        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return false;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Response: {"data":[{"room_id":"xxx","alive":true}],"status_code":0}
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return false;

        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("alive", out var alive) && alive.GetBoolean())
                return true;
        }

        return false;
    }
}
