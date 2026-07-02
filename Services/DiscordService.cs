using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class DiscordService : IDiscordService
{
    private readonly DiscordSocketClient _client;
    private readonly BotConfiguration _config;
    private readonly ILogger<DiscordService> _logger;

    // TaskCompletionSource để các service khác có thể await cho đến khi
    // Discord thực sự ready (Client.Ready event fired)
    private readonly TaskCompletionSource<bool> _readyTcs = new();

    // Circuit breaker: đếm số lần 401 liên tiếp.
    // 401 = token invalid/revoked → không retry vô hạn.
    // Sau 3 lần → exit process để Docker restart (tránh crash loop).
    private int _consecutiveAuthFailures = 0;
    private const int MaxAuthFailures = 3;

    /// <summary>
    /// Chờ đến khi Discord Ready. Nếu sau 60s vẫn chưa ready → throw TimeoutException
    /// để tránh background services treo vĩnh viễn khi Discord connection thất bại.
    /// </summary>
    public async Task WaitForReadyAsync()
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(60));
        var completed = await Task.WhenAny(_readyTcs.Task, timeout);
        if (completed == timeout)
            throw new TimeoutException("Discord Ready event không nhận được sau 60s — kiểm tra token và kết nối mạng.");
    }

    public DiscordSocketClient Client => _client;

    public DiscordService(
        DiscordSocketClient client,
        IOptions<BotConfiguration> config,
        ILogger<DiscordService> logger)
    {
        _client = client;
        _config = config.Value;
        _logger = logger;

        _client.Log += OnDiscordLogAsync;
        _client.Ready += OnReadyAsync;
        _client.Disconnected += OnDisconnectedAsync;
    }

    // ── Connect ──────────────────────────────────────────────────────────────

    public async Task ConnectAsync()
    {
        _logger.LogInformation("Đang kết nối Discord...");

        try
        {
            await _client.LoginAsync(TokenType.Bot, _config.DiscordToken);
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // 401 ngay tại LoginAsync → token sai hoàn toàn, không cần retry.
            _logger.LogCritical(
                "Discord LoginAsync thất bại: 401 Unauthorized — token không hợp lệ. " +
                "Kiểm tra DISCORD_TOKEN trong .env rồi restart thủ công.");
            Serilog.Log.CloseAndFlush();
            Environment.Exit(1);
            return; // Unreachable, nhưng cần để compiler không cảnh báo
        }

        await _client.StartAsync();

        // Chờ tối đa 30 giây cho Ready event
        // Nếu quá 30s → log warning nhưng không crash app
        var timeout = Task.Delay(TimeSpan.FromSeconds(30));
        var completed = await Task.WhenAny(_readyTcs.Task, timeout);

        if (completed == timeout)
            _logger.LogWarning("Discord Ready event chưa nhận được sau 30s");

    }

    // ── Notifications ────────────────────────────────────────────────────────

    public async Task SendVideoNotificationAsync(VideoInfo video)
    {
        try
        {
            var isLive = video.IsLive;
            var roleId = isLive ? _config.LiveRoleId : _config.VideoRoleId;
            var channelId = isLive ? _config.LiveChannelId : _config.VideoChannelId;

            var url = $"https://www.youtube.com/watch?v={video.VideoId}";

            // AllowedMentions cần set RoleIds hoặc AllowedTypes — không set cả 2
            // Discord.Net sẽ throw nếu set AllowedTypes.Roles + RoleIds cùng lúc
            var allowedMentions = roleId != 0
                ? new AllowedMentions { RoleIds = new List<ulong> { roleId } }
                : new AllowedMentions { AllowedTypes = AllowedMentionTypes.None };

            var mention = roleId != 0 ? $"<@&{roleId}>\n\n" : string.Empty;

            var body = isLive
                ? $"🔴 Tôi đang live rồi anh em ơi:\n\n{url}"
                : $"📺 Video mới lên sóng:\n\n{url}";

            await SendToChannelAsync(
                channelId,
                text: mention + body,
                allowedMentions: allowedMentions);

            _logger.LogInformation(
                "Notification sent — {Title} ({Type})",
                video.Title, isLive ? "LIVE" : "VIDEO");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendVideoNotificationAsync thất bại — {VideoId}", video.VideoId);
        }
    }

    // ── Core Send ────────────────────────────────────────────────────────────

    public async Task SendToChannelAsync(
        ulong channelId,
        string? text = null,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        if (channelId == 0)
        {
            _logger.LogWarning("Channel ID = 0, bỏ qua gửi message");
            return;
        }

        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            _logger.LogWarning("Không tìm thấy channel {ChannelId}", channelId);
            return;
        }

        await channel.SendMessageAsync(
            text: string.IsNullOrWhiteSpace(text) ? null : text,
            embed: embed,
            components: components,
            allowedMentions: allowedMentions);

        _logger.LogDebug("Message sent to channel {ChannelId}", channelId);
    }

    // ── Event Handlers ───────────────────────────────────────────────────────

    private async Task OnReadyAsync()
    {
        _readyTcs.TrySetResult(true);
        _consecutiveAuthFailures = 0; // Reset circuit breaker khi kết nối thành công
        _logger.LogInformation(
            "Discord Ready — logged in as {Username}#{Discriminator}",
            _client.CurrentUser.Username,
            _client.CurrentUser.Discriminator);

        await SendStartupNotificationAsync();
    }

    /// <summary>
    /// Gửi embed thông báo khi bot khởi động / restart.
    /// Chỉ gửi nếu StatusChannelId != 0.
    /// </summary>
    private async Task SendStartupNotificationAsync()
    {
        if (_config.StatusChannelId == 0) return;

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            var embed = new EmbedBuilder()
                .WithTitle("🤖 Bot đã khởi động")
                .WithDescription(
                    $"**{_client.CurrentUser.Username}** đã kết nối thành công và sẵn sàng hoạt động.")
                .WithColor(Color.Green)
                .AddField("Phiên bản", $"`v{version}`", inline: true)
                .AddField("Thời gian", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>", inline: true)
                .WithCurrentTimestamp()
                .Build();

            await SendToChannelAsync(_config.StatusChannelId, embed: embed);
            _logger.LogInformation(
                "Startup notification sent → StatusChannel {ChannelId}", _config.StatusChannelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SendStartupNotificationAsync thất bại");
        }
    }

    private Task OnDisconnectedAsync(Exception? ex)
    {
        // ✅ Circuit breaker: nếu Discord trả về 401 liên tiếp → token invalid.
        // Không retry vô hạn — exit để Docker restart với token đúng.
        if (ex is Discord.Net.HttpException { HttpCode: System.Net.HttpStatusCode.Unauthorized })
        {
            _consecutiveAuthFailures++;
            _logger.LogWarning(
                "Discord 401 Unauthorized (lần {Count}/{Max}) — token có thể không hợp lệ",
                _consecutiveAuthFailures, MaxAuthFailures);

            if (_consecutiveAuthFailures >= MaxAuthFailures)
            {
                _logger.LogCritical(
                    "Discord 401 Unauthorized {Max} lần liên tiếp — token không hợp lệ, dừng bot. " +
                    "Kiểm tra DISCORD_TOKEN trong .env rồi restart thủ công.",
                    MaxAuthFailures);
                // Flush log trước khi exit
                Serilog.Log.CloseAndFlush();
                Environment.Exit(1);
            }
            return Task.CompletedTask;
        }

        // ✅ FIX: Phân biệt loại disconnect để tránh log noise.
        //
        // TaskCanceledException / OperationCanceledException:
        //   → Discord.Net internal cancellation khi reconnect hoặc heartbeat timeout.
        //   → Hoàn toàn bình thường, Discord.Net tự xử lý, log Debug để không gây alarm.
        //
        // GatewayReconnectException:
        //   → Server yêu cầu reconnect (gateway rotation, deploy Discord,...).
        //   → Bình thường, log Information.
        //
        // Các exception khác (network lỗi thật, token invalid,...):
        //   → Log Warning với full exception để debug.
        switch (ex)
        {
            case TaskCanceledException or OperationCanceledException:
                _logger.LogDebug("Discord connection cancelled (internal reconnect) — Discord.Net sẽ tự reconnect");
                break;

            case Discord.WebSocket.GatewayReconnectException:
                _logger.LogInformation("Discord server requested reconnect — Discord.Net sẽ tự reconnect");
                break;

            case null:
                _logger.LogInformation("Discord disconnected (no exception) — Discord.Net sẽ tự reconnect");
                break;

            default:
                _logger.LogWarning(ex, "Discord disconnected — Discord.Net sẽ tự reconnect");
                break;
        }

        return Task.CompletedTask;
    }

    private Task OnDiscordLogAsync(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            LogSeverity.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogSeverity.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogSeverity.Info => Microsoft.Extensions.Logging.LogLevel.Information,
            LogSeverity.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace,
            LogSeverity.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };

        // ✅ FIX: Discord.Net log TaskCanceledException ở Warning level gây noise.
        // Downgrade xuống Debug vì đây là internal reconnect mechanism, không phải lỗi.
        if (msg.Exception is TaskCanceledException or OperationCanceledException)
            level = Microsoft.Extensions.Logging.LogLevel.Debug;

        _logger.Log(level, msg.Exception, "[Discord.Net] {Message}", msg.Message);
        return Task.CompletedTask;
    }
}