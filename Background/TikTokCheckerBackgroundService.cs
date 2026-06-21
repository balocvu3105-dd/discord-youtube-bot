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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TikTok check thất bại cho @{Username}, bỏ qua lần này", username);
                continue;
            }

            var wasNotified = _liveNotified.GetValueOrDefault(username, false);

            if (isLive && !wasNotified)
            {
                // Live bắt đầu → gửi thông báo
                await SendLiveNotificationAsync(username);
                _liveNotified[username] = true;
                stateChanged = true;
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
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể lưu TikTok live state");
        }
    }
}
