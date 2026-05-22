using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class DiscordService
{
    private readonly DiscordSocketClient _client;

    public DiscordSocketClient Client => _client;

    private readonly BotConfiguration _config;
    private readonly ILogger<DiscordService> _logger;

    public DiscordService(
    DiscordSocketClient client,
    IOptions<BotConfiguration> config,
    ILogger<DiscordService> logger)
    {
        _client = client;

        _config = config.Value;

        _logger = logger;

        _client.Log += OnDiscordLogAsync;
    }
    // =========================================================
    // CONNECT
    // =========================================================

    public async Task ConnectAsync()
    {
        try
        {
            _logger.LogInformation(
                "Đang kết nối Discord...");

            if (string.IsNullOrWhiteSpace(_config.DiscordToken))
                throw new Exception(
                    "DiscordToken bị trống!");

            await _client.LoginAsync(
                TokenType.Bot,
                _config.DiscordToken);

            await _client.StartAsync();

            var readyTask =
                new TaskCompletionSource<bool>();

            _client.Ready += () =>
            {
                readyTask.TrySetResult(true);

                _logger.LogInformation(
                    "✅ Đăng nhập Discord thành công: {Name}",
                    _client.CurrentUser.Username);

                return Task.CompletedTask;
            };

            await Task.WhenAny(
                readyTask.Task,
                Task.Delay(TimeSpan.FromSeconds(30)));

            _logger.LogInformation(
                "✅ Discord connected");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Không thể kết nối Discord");
        }
    }

    // =========================================================
    // VIDEO / LIVE NOTIFICATION
    // =========================================================

    public async Task SendVideoNotificationAsync(
        VideoInfo video)
    {
        try
        {
            string url =
                $"https://www.youtube.com/watch?v={video.VideoId}";

            ulong roleId =
                video.LiveBroadcastContent == "live"
                    ? _config.LiveRoleId
                    : _config.VideoRoleId;

            string mention =
                roleId != 0
                    ? $"<@&{roleId}>"
                    : string.Empty;

            // FIX:
            // Không dùng AllowedTypes.Roles cùng lúc với RoleIds
            var allowedMentions =
                roleId != 0
                    ? new AllowedMentions
                    {
                        RoleIds = new List<ulong>
                        {
                        roleId
                        }
                    }
                    : new AllowedMentions
                    {
                        AllowedTypes = AllowedMentionTypes.None
                    };

            string messageBody =
                video.LiveBroadcastContent == "live"
                    ? "🔴 Tôi đang live rồi anh em ơi:\n\n" + url
                    : "📺 Video mới lên sóng:\n\n" + url;

            string finalMessage =
                mention.Length > 0
                    ? mention + "\n\n" + messageBody
                    : messageBody;

            ulong channelId =
                video.LiveBroadcastContent == "live"
                    ? _config.LiveChannelId
                    : _config.VideoChannelId;

            await SendToChannelByIdAsync(
                channelId,
                finalMessage,
                allowedMentions: allowedMentions);

            _logger.LogInformation(
                "✅ Video notification sent: {Title}",
                video.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ SendVideoNotificationAsync failed");
        }
    }

    // =========================================================
    // SHOP MESSAGE
    // =========================================================

    public async Task SendShopAsync(
        Embed embed,
        MessageComponent? components = null)
    {
        try
        {
            await SendToChannelByIdAsync(
                _config.ShopChannelId,
                string.Empty,
                embed,
                components);

            _logger.LogInformation(
                "✅ Shop message sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ SendShopAsync failed");
        }
    }

    // =========================================================
    // CORE SEND METHOD
    // =========================================================

    public async Task SendToChannelByIdAsync(
        ulong channelId,
        string message,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        // Guard: channel chưa cấu hình
        if (channelId == 0)
        {
            _logger.LogWarning(
                "⚠️ Channel ID = 0, bỏ qua gửi message");

            return;
        }

        var channel =
            _client.GetChannel(channelId)
                as IMessageChannel;

        // Guard: không tìm thấy channel
        if (channel == null)
        {
            _logger.LogWarning(
                "❌ Không tìm thấy channel ID: {ChannelId}",
                channelId);

            return;
        }

        await channel.SendMessageAsync(
            text: string.IsNullOrWhiteSpace(message)
                ? null
                : message,

            embed: embed,
            components: components,
            allowedMentions: allowedMentions);

        _logger.LogInformation(
            "✅ Sent message to channel {ChannelId}",
            channelId);
    }

    // =========================================================
    // DISCORD LOG
    // =========================================================

    private Task OnDiscordLogAsync(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:

                _logger.LogError(
                    msg.Exception,
                    "[Discord] {Message}",
                    msg.Message);

                break;

            case LogSeverity.Warning:

                _logger.LogWarning(
                    msg.Exception,
                    "[Discord] {Message}",
                    msg.Message);

                break;

            default:

                _logger.LogInformation(
                    "[Discord] {Message}",
                    msg.Message);

                break;
        }

        return Task.CompletedTask;
    }

}