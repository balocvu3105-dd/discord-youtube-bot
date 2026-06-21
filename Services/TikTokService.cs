using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Kiểm tra TikTok live status qua webcast API (webcast.tiktok.com).
/// Cần TikTokMsToken từ cookie trình duyệt để bypass xác thực API.
///
/// Cách lấy msToken:
///   1. Mở tiktok.com trong Chrome
///   2. F12 → Application → Cookies → https://www.tiktok.com
///   3. Copy giá trị cookie "msToken"
///   4. Paste vào appsettings.json → BotConfiguration.TikTokMsToken
/// </summary>
public class TikTokService : ITikTokService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TikTokService> _logger;
    private readonly BotConfiguration _config;

    private const string WebcastBase = "https://webcast.tiktok.com/webcast";
    private const string Aid = "1988";

    public TikTokService(
        HttpClient httpClient,
        IOptions<BotConfiguration> config,
        ILogger<TikTokService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<bool> IsLiveAsync(string username)
    {
        try
        {
            if (string.IsNullOrEmpty(_config.TikTokMsToken))
            {
                _logger.LogWarning("TikTok @{Username} — TikTokMsToken chưa được cấu hình trong appsettings.json. " +
                    "Vui lòng lấy msToken từ cookie trình duyệt tiktok.com và thêm vào config.", username);
                return false;
            }

            var (roomId, isLiveFromInfo) = await GetRoomInfoAsync(username);

            if (isLiveFromInfo.HasValue)
            {
                _logger.LogInformation("TikTok @{Username} — room/info: roomId={RoomId}, live={Live}",
                    username, roomId ?? "null", isLiveFromInfo.Value);
                return isLiveFromInfo.Value;
            }

            // Có roomId nhưng status không rõ → ping check_alive
            if (!string.IsNullOrEmpty(roomId))
            {
                var alive = await CheckRoomAliveAsync(roomId);
                _logger.LogInformation("TikTok @{Username} — check_alive: roomId={RoomId}, alive={Alive}",
                    username, roomId, alive);
                return alive;
            }

            _logger.LogInformation("TikTok @{Username} — không xác định được trạng thái live", username);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TikTokService.IsLiveAsync thất bại cho @{Username}", username);
            return false;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<(string? roomId, bool? isLive)> GetRoomInfoAsync(string username)
    {
        var msToken = Uri.EscapeDataString(_config.TikTokMsToken);
        var url = $"{WebcastBase}/room/info/?aid={Aid}&app_language=en&device_platform=web" +
                  $"&browser_language=en&browser_platform=Win32" +
                  $"&browser_name=Mozilla&browser_version=5.0%20(Windows%20NT%2010.0%3B%20Win64%3B%20x64)" +
                  $"&browser_online=true&cookie_enabled=1" +
                  $"&screen_width=1920&screen_height=1080" +
                  $"&webcast_sdk_version=1.9.5&update_version_code=1.9.5" +
                  $"&msToken={msToken}" +
                  $"&uniqueId={Uri.EscapeDataString(username)}";

        using var response = await _httpClient.GetAsync(url);

        _logger.LogInformation("TikTok @{Username} — room/info HTTP {Status}", username, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode) return (null, null);

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("TikTok @{Username} — room/info preview: {Preview}",
            username, body.Length > 300 ? body[..300] : body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("status_code", out var sc) && sc.GetInt32() != 0)
        {
            _logger.LogInformation("TikTok @{Username} — room/info status_code={Code} (không live hoặc token hết hạn)",
                username, sc.GetInt32());
            return (null, false);
        }

        if (!root.TryGetProperty("data", out var data))
            return (null, null);

        string? roomId = null;

        if (data.TryGetProperty("room", out var room))
        {
            if (room.TryGetProperty("id", out var idProp))
                roomId = idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : idProp.GetInt64().ToString();

            // status=2 → live; status=4 → ended/offline
            if (room.TryGetProperty("status", out var statusProp))
                return (roomId, statusProp.GetInt32() == 2);
        }

        if (data.TryGetProperty("room_id", out var rid))
            roomId ??= rid.ValueKind == JsonValueKind.String
                ? rid.GetString()
                : rid.GetInt64().ToString();

        return (roomId, null);
    }

    private async Task<bool> CheckRoomAliveAsync(string roomId)
    {
        var url = $"{WebcastBase}/room/check_alive/?aid={Aid}&room_ids={roomId}";

        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return false;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

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
