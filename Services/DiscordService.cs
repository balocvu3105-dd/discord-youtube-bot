using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    public Task WaitForReadyAsync() => _readyTcs.Task;

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

        await _client.LoginAsync(TokenType.Bot, _config.DiscordToken);
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

    private Task OnReadyAsync()
    {
        _readyTcs.TrySetResult(true);
        _logger.LogInformation(
            "Discord Ready — logged in as {Username}#{Discriminator}",
            _client.CurrentUser.Username,
            _client.CurrentUser.Discriminator);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception? ex)
    {
        // Discord.Net tự reconnect, ta chỉ log để biết
        _logger.LogWarning(ex, "Discord disconnected — Discord.Net sẽ tự reconnect");
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

        _logger.Log(level, msg.Exception, "[Discord.Net] {Message}", msg.Message);
        return Task.CompletedTask;
    }
}