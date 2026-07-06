using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Background;

/// <summary>
/// Bộ điều phối trung tâm (Coordinator) kiểm tra livestream đa nền tảng (Twitch, Kick, Facebook...).
/// Tự động quản lý chu kỳ kiểm tra, đồng bộ trạng thái khi khởi động, chống spam thông báo lặp và quản lý lỗi mạng.
/// </summary>
public class UnifiedStreamCheckerBackgroundService : BackgroundService
{
    private readonly IDiscordService _discord;
    private readonly BotConfiguration _config;
    private readonly StreamerManagerService _streamerManager;
    private readonly IEnumerable<IStreamPlatformProvider> _providers;
    private readonly ILogger<UnifiedStreamCheckerBackgroundService> _logger;

    private const int DefaultIntervalSeconds = 60;

    public UnifiedStreamCheckerBackgroundService(
        IDiscordService discord,
        IOptions<BotConfiguration> config,
        StreamerManagerService streamerManager,
        IEnumerable<IStreamPlatformProvider> providers,
        ILogger<UnifiedStreamCheckerBackgroundService> logger)
    {
        _discord = discord;
        _config = config.Value;
        _streamerManager = streamerManager;
        _providers = providers;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UnifiedStreamCheckerBackgroundService starting — quản lý các nền tảng mở rộng");

        await _discord.WaitForReadyAsync();
        _logger.LogInformation("Discord ready — UnifiedStreamCheckerBackgroundService running");

        await SyncStateOnStartupAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(Math.Max(30, DefaultIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await CheckAllStreamersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UnifiedStreamCheckerBackgroundService — unhandled exception during check loop");
            }
        }
    }

    /// <summary>
    /// Khi bot restart: kiểm tra kênh nào đang live mà chưa ghi nhận trong disk -> gán cờ notified = true
    /// KHÔNG gửi thông báo để tránh lặp lại thông báo khi deploy/restart bot.
    /// </summary>
    private async Task SyncStateOnStartupAsync(CancellationToken ct)
    {
        _logger.LogInformation("Đồng bộ trạng thái live đa nền tảng khi khởi động...");
        var state = await _streamerManager.LoadStateAsync();
        var streamers = await _streamerManager.GetAllTrackedStreamersAsync();
        var changed = false;

        foreach (var streamer in streamers)
        {
            if (ct.IsCancellationRequested) break;

            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.PlatformName, streamer.Platform, StringComparison.OrdinalIgnoreCase));

            // Bỏ qua nếu không có provider (ví dụ YouTube/TikTok do service cũ quản lý)
            if (provider is null) continue;

            try
            {
                var status = await provider.CheckLiveStatusAsync(streamer.UsernameOrId, ct);
                var isNotified = state.LiveNotifiedStates.GetValueOrDefault(streamer.Key, false);

                if (status.IsLive && !isNotified)
                {
                    _logger.LogInformation("[Startup Sync] Phát hiện @{User} ({Platform}) đang live -> đánh dấu đã thông báo (KHÔNG gửi lại)",
                        streamer.UsernameOrId, provider.PlatformName);
                    state.LiveNotifiedStates[streamer.Key] = true;
                    if (!string.IsNullOrEmpty(status.StreamId))
                        state.LastStreamIds[streamer.Key] = status.StreamId;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Startup Sync] Lỗi kiểm tra @{User} ({Platform}), bỏ qua",
                    streamer.UsernameOrId, streamer.Platform);
            }
        }

        if (changed)
            await _streamerManager.SaveStateAsync(state);

        _logger.LogInformation("Đồng bộ trạng thái startup hoàn tất");
    }

    private async Task CheckAllStreamersAsync(CancellationToken ct)
    {
        var state = await _streamerManager.LoadStateAsync();
        var streamers = await _streamerManager.GetAllTrackedStreamersAsync();
        var changed = false;

        foreach (var streamer in streamers)
        {
            if (ct.IsCancellationRequested) break;

            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.PlatformName, streamer.Platform, StringComparison.OrdinalIgnoreCase));

            if (provider is null) continue;

            try
            {
                var status = await provider.CheckLiveStatusAsync(streamer.UsernameOrId, ct);
                var isNotified = state.LiveNotifiedStates.GetValueOrDefault(streamer.Key, false);
                var lastStreamId = state.LastStreamIds.GetValueOrDefault(streamer.Key, string.Empty);

                if (status.IsLive)
                {
                    // Chỉ gửi thông báo nếu chưa thông báo cho buổi live này VÀ (stream ID mới khác stream ID cũ hoặc không có ID)
                    if (!isNotified && (string.IsNullOrEmpty(status.StreamId) || status.StreamId != lastStreamId))
                    {
                        await SendLiveNotificationAsync(streamer, status, provider);
                        state.LiveNotifiedStates[streamer.Key] = true;
                        if (!string.IsNullOrEmpty(status.StreamId))
                            state.LastStreamIds[streamer.Key] = status.StreamId;
                        changed = true;
                    }
                }
                else
                {
                    if (isNotified)
                    {
                        _logger.LogInformation("[{Platform}] @{User} đã kết thúc livestream -> reset trạng thái",
                            provider.PlatformName, streamer.UsernameOrId);
                        state.LiveNotifiedStates[streamer.Key] = false;
                        changed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Khi lỗi mạng/timeout/API rate-limit: Ghi log cảnh báo và bỏ qua tick này
                // KHÔNG reset trạng thái LiveNotifiedStates về false để tránh lỗi double notify
                _logger.LogWarning(ex, "[{Platform}] Kiểm tra live thất bại cho @{User}, bỏ qua lần này",
                    provider.PlatformName, streamer.UsernameOrId);
            }
        }

        if (changed)
            await _streamerManager.SaveStateAsync(state);
    }

    private async Task SendLiveNotificationAsync(StreamerConfigItem streamer, StreamStatusResult status, IStreamPlatformProvider provider)
    {
        var channelId = streamer.TargetChannelId != 0 ? streamer.TargetChannelId : _config.LiveChannelId;
        var roleId = streamer.TargetRoleId != 0 ? streamer.TargetRoleId : _config.LiveRoleId;

        if (channelId == 0)
        {
            _logger.LogWarning("[{Platform}] Không có LiveChannelId để gửi thông báo cho @{User}", provider.PlatformName, streamer.UsernameOrId);
            return;
        }

        var mention = roleId != 0 ? $"<@&{roleId}>\n\n" : string.Empty;
        var displayName = !string.IsNullOrWhiteSpace(streamer.CustomName) ? streamer.CustomName : streamer.UsernameOrId;

        var color = uint.TryParse(provider.PlatformColorHex, System.Globalization.NumberStyles.HexNumber, null, out var hex)
            ? new Discord.Color(hex)
            : Discord.Color.Purple;

        var embed = new Discord.EmbedBuilder()
            .WithAuthor($"{provider.PlatformEmoji} {displayName} đang phát trực tiếp trên {provider.PlatformName}!", url: status.StreamUrl)
            .WithTitle(status.Title)
            .WithUrl(status.StreamUrl)
            .WithColor(color)
            .WithFooter($"Nền tảng: {provider.PlatformName} • Cập nhật lúc")
            .WithCurrentTimestamp();

        if (!string.IsNullOrWhiteSpace(status.ThumbnailUrl))
        {
            embed.WithImageUrl(status.ThumbnailUrl);
        }

        if (status.ViewerCount > 0)
        {
            embed.AddField("👥 Người xem", $"{status.ViewerCount:N0}", inline: true);
        }

        var components = new Discord.ComponentBuilder()
            .WithButton("🔴 Xem Live Ngay!", style: Discord.ButtonStyle.Link, url: status.StreamUrl)
            .Build();

        var allowedMentions = roleId != 0
            ? new Discord.AllowedMentions { RoleIds = new List<ulong> { roleId } }
            : new Discord.AllowedMentions { AllowedTypes = Discord.AllowedMentionTypes.None };

        await _discord.SendToChannelAsync(channelId, text: mention + $"{provider.PlatformEmoji} **{displayName}** vừa bật livestream!", embed: embed.Build(), components: components, allowedMentions: allowedMentions);
        _logger.LogInformation("[{Platform}] Đã gửi thông báo live cho @{User}", provider.PlatformName, streamer.UsernameOrId);
    }
}
