using Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Background;

/// <summary>
/// Background service poll TikTok mỗi TikTokCheckIntervalSeconds giây.
/// Gửi thông báo Discord khi user bắt đầu live, clear state khi kết thúc live.
///
/// State: Dictionary&lt;string, bool&gt; username → đã gửi thông báo chưa.
/// Lưu vào TikTokLiveStateFilePath để persist qua restart.
/// </summary>
public class TikTokCheckerBackgroundService : BackgroundService
{
    private readonly IDiscordService _discord;
    private readonly ITikTokService _tiktok;
    private readonly BotConfiguration _config;
    private readonly ILogger<TikTokCheckerBackgroundService> _logger;

    /// <summary>username → đã notify live chưa</summary>
    private Dictionary<string, bool> _liveNotified = new();

    public TikTokCheckerBackgroundService(
        IDiscordService discord,
        ITikTokService tiktok,
        IOptions<BotConfiguration> config,
        ILogger<TikTokCheckerBackgroundService> logger)
    {
        _discord = discord;
        _tiktok = tiktok;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TikTokCheckerBackgroundService — ExecuteAsync started");

        if (_config.TikTokUsernames.Count == 0)
        {
            _logger.LogInformation("TikTokCheckerBackgroundService — không có username nào, bỏ qua");
            return;
        }

        _logger.LogInformation(
            "TikTokCheckerBackgroundService starting — {Count} user(s), Interval={Seconds}s",
            _config.TikTokUsernames.Count, _config.TikTokCheckIntervalSeconds);

        await _discord.WaitForReadyAsync();

        // Load state từ file nếu có
        await LoadStateAsync();

        // ✅ FIX DOUBLE NOTIFY: Sau khi load state, đồng bộ với TikTok API.
        // Nếu state file bị mất/corrupt mà user đang live → state = false
        // → tick đầu tiên sẽ gửi thông báo lại.
        // SyncStateOnStartupAsync phát hiện live → set state=true (không gửi) → tránh duplicate.
        await SyncStateOnStartupAsync();

        _logger.LogInformation("Discord ready — TikTokCheckerBackgroundService running");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTikTokAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TikTokCheckerBackgroundService — unhandled exception");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_config.TikTokCheckIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Kiểm tra TikTok API ngay khi startup — nếu user đang live mà state = false
    /// (do file bị mất/corrupt hoặc bot restart giữa live), cập nhật state = true mà KHÔNG gửi thông báo.
    /// Tránh double notification khi bot restart trong lúc user đang live.
    /// </summary>
    private async Task SyncStateOnStartupAsync()
    {
        var changed = false;
        foreach (var username in _config.TikTokUsernames)
        {
            if (string.IsNullOrWhiteSpace(username)) continue;
            try
            {
                var isLive = await _tiktok.IsLiveAsync(username);
                if (isLive && !_liveNotified.GetValueOrDefault(username, false))
                {
                    // Đang live nhưng state chưa được đánh dấu → đánh dấu không gửi thông báo.
                    // Bot đã gửi thông báo trước khi restart (hoặc user live mà bot vừa khởi động).
                    // Không gửi lại — chỉ đồng bộ state.
                    _liveNotified[username] = true;
                    changed = true;
                    _logger.LogInformation(
                        "Startup sync: @{Username} đang live — set state=true, không gửi thông báo", username);
                }
                else if (!isLive && _liveNotified.GetValueOrDefault(username, false))
                {
                    // State = true nhưng không còn live → reset để tránh bỏ sót live kế tiếp.
                    _liveNotified[username] = false;
                    changed = true;
                    _logger.LogInformation(
                        "Startup sync: @{Username} không live — reset state=false", username);
                }
                else
                {
                    _logger.LogDebug(
                        "Startup sync: @{Username} — isLive={IsLive}, state OK", username, isLive);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tiktok_check.py exited with code 1"))
            {
                _logger.LogWarning("Startup sync TikTok tạm thời gián đoạn cho @{Username} ({Reason}) — giữ state cũ", username, ex.Message);
            }
            catch (Exception ex)
            {
                // Lỗi check TikTok khi startup → giữ nguyên state hiện tại, tiếp tục chạy.
                _logger.LogWarning(ex, "Startup sync TikTok thất bại cho @{Username} — giữ state cũ", username);
            }
        }

        if (changed)
            await SaveStateAsync();
    }

    private async Task CheckTikTokAsync(CancellationToken ct)
    {
        var stateChanged = false;

        foreach (var username in _config.TikTokUsernames)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(username)) continue;

            bool isLive;
            try
            {
                isLive = await _tiktok.IsLiveAsync(username);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tiktok_check.py exited with code 1"))
            {
                _logger.LogWarning("TikTok check tạm thời gián đoạn cho @{Username} ({Reason}) — bỏ qua lần này", username, ex.Message);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TikTok check thất bại cho @{Username}, bỏ qua lần này", username);
                continue;
            }

            var wasNotified = _liveNotified.GetValueOrDefault(username, false);

            if (isLive && !wasNotified)
            {
                // Live bắt đầu → gửi thông báo, save ngay để tránh duplicate khi bot restart
                await SendLiveNotificationAsync(username);
                _liveNotified[username] = true;
                await SaveStateAsync();   // ← save ngay, không chờ cuối loop
                stateChanged = false;     // đã save rồi, đánh dấu không cần save lại
                _logger.LogInformation("TikTok LIVE notification sent — @{Username}", username);
            }
            else if (!isLive && wasNotified)
            {
                // Live kết thúc → reset state
                _liveNotified[username] = false;
                stateChanged = true;
                _logger.LogInformation("TikTok live ended — reset state @{Username}", username);
            }
            else
            {
                _logger.LogDebug("TikTok @{Username} — isLive={IsLive}, wasNotified={WasNotified} (no change)",
                    username, isLive, wasNotified);
            }
        }

        if (stateChanged)
            await SaveStateAsync();
    }

    private async Task SendLiveNotificationAsync(string username)
    {
        try
        {
            var channelId = _config.TikTokLiveChannelId;
            if (channelId == 0)
            {
                _logger.LogWarning("TikTokLiveChannelId = 0 — bỏ qua gửi thông báo @{Username}", username);
                return;
            }

            var roleId = _config.TikTokLiveRoleId;
            var allowedMentions = roleId != 0
                ? new AllowedMentions { RoleIds = new List<ulong> { roleId } }
                : new AllowedMentions { AllowedTypes = AllowedMentionTypes.None };

            var mention = roleId != 0 ? $"<@&{roleId}>\n\n" : string.Empty;
            var liveUrl = $"https://www.tiktok.com/@{username}/live";
            var body = $"🔴 **@{username}** đang live trên TikTok!\n\n{liveUrl}";

            await _discord.SendToChannelAsync(
                channelId,
                text: mention + body,
                allowedMentions: allowedMentions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendLiveNotificationAsync thất bại — @{Username}", username);
        }
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private async Task LoadStateAsync()
    {
        try
        {
            var path = _config.TikTokLiveStateFilePath;
            if (!File.Exists(path)) return;

            var json = await File.ReadAllTextAsync(path);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (loaded != null)
            {
                _liveNotified = loaded;
                _logger.LogInformation("TikTok live state loaded — {Count} entries", _liveNotified.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể load TikTok live state, dùng state rỗng");
        }
    }

    private async Task SaveStateAsync()
    {
        try
        {
            var path = _config.TikTokLiveStateFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = System.Text.Json.JsonSerializer.Serialize(_liveNotified,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            // Atomic write: ghi vào temp rồi rename — tránh corrupt JSON nếu process crash giữa chừng
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể lưu TikTok live state");
        }
    }
}
