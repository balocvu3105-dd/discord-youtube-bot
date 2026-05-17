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
        IOptions<BotConfiguration> config,
        ILogger<DiscordService> logger)
    {
        _config = config.Value;
        _logger = logger;

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds |
                GatewayIntents.GuildMessages,
            LogGatewayIntentWarnings = false
        };

        _client = new DiscordSocketClient(socketConfig);
        _client.Log += OnDiscordLogAsync;
    }

    // =========================================================
    // CONNECT
    // =========================================================

    public async Task ConnectAsync()
    {
        try
        {
            _logger.LogInformation("Đang kết nối vào Discord...");

            if (string.IsNullOrWhiteSpace(_config.DiscordToken))
                throw new Exception("DiscordToken bị trống!");

            _logger.LogInformation("Token loaded: OK ✅");

            await _client.LoginAsync(TokenType.Bot, _config.DiscordToken);
            await _client.StartAsync();

            var readyTask = new TaskCompletionSource<bool>();

            _client.Ready += () =>
            {
                readyTask.TrySetResult(true);
                _logger.LogInformation(
                    "Đã đăng nhập với tên: {Name}",
                    _client.CurrentUser.Username);
                return Task.CompletedTask;
            };

            await Task.WhenAny(
                readyTask.Task,
                Task.Delay(TimeSpan.FromSeconds(30)));

            _logger.LogInformation("Bot Discord đã kết nối.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Không thể kết nối Discord");
        }
    }

    // =========================================================
    // SEND VIDEO / LIVE
    // =========================================================

    public async Task SendVideoNotificationAsync(VideoInfo video)
    {
        try
        {
            string url =
                $"https://www.youtube.com/watch?v={video.VideoId}";

            // LIVE
            if (video.LiveBroadcastContent == "live")
            {
                _logger.LogInformation(
                    "📡 Gửi thông báo LIVE: {Title}",
                    video.Title);

                // Nếu có config LiveRoleId → tag role đó
                // Nếu không (= 0) → không tag ai cả
                // Lý do dùng ulong: Discord ID là số nguyên 64-bit rất lớn
                string mention = _config.LiveRoleId != 0
                    ? $"<@&{_config.LiveRoleId}>"
                    : string.Empty;

                // AllowedMentions kiểm soát Discord có THỰC SỰ ping không
                // Dù text có <@&123> nhưng nếu không khai báo ở đây → Discord bỏ qua
                var allowedMentions = _config.LiveRoleId != 0
                    ? new AllowedMentions
                    {
                        RoleIds = new List<ulong> { _config.LiveRoleId }
                    }
                    : AllowedMentions.None;

                string liveMessage =
                    (mention.Length > 0 ? mention + "\n\n" : "") +
                    "🔴 Tôi đang live rồi anh em ơi:\n\n" + url;

                await SendToChannelByIdAsync(
                    _config.LiveChannelId,
                    liveMessage,
                    allowedMentions: allowedMentions);

                return;
            }

            // NORMAL VIDEO
            _logger.LogInformation(
                "📡 Gửi thông báo VIDEO: {Title}",
                video.Title);

            string videoMention = _config.VideoRoleId != 0
                ? $"<@&{_config.VideoRoleId}>"
                : string.Empty;

            var videoAllowedMentions = _config.VideoRoleId != 0
                ? new AllowedMentions
                {
                    RoleIds = new List<ulong> { _config.VideoRoleId }
                }
                : AllowedMentions.None;

            string videoMessage =
                (videoMention.Length > 0 ? videoMention + "\n\n" : "") +
                "📺 Video mới lên sóng:\n\n" + url;

            await SendToChannelByIdAsync(
                _config.VideoChannelId,
                videoMessage,
                allowedMentions: videoAllowedMentions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SendVideoNotificationAsync failed");
        }
    }

    // =========================================================
    // SEND PROMO
    // =========================================================

    public async Task SendPromoAsync(
        Embed embed,
        MessageComponent? components = null)
    {
        try
        {
            await SendToChannelByIdAsync(
                _config.PromoChannelId,
                string.Empty,
                embed,
                components);

            _logger.LogInformation("✅ Promo sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SendPromoAsync failed");
        }
    }

    // =========================================================
    // SEND SHOP INFO
    // =========================================================

    public async Task SendShopInfoAsync(
        Embed embed,
        MessageComponent? components = null)
    {
        try
        {
            await SendToChannelByIdAsync(
                _config.ShopInfoChannelId,
                string.Empty,
                embed,
                components);

            _logger.LogInformation("✅ ShopInfo sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SendShopInfoAsync failed");
        }
    }

    // =========================================================
    // CORE: SEND BY CHANNEL ID
    // =========================================================

    public async Task SendToChannelByIdAsync(
        ulong channelId,
        string message,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        // Guard: chưa cấu hình ID
        if (channelId == 0)
        {
            _logger.LogWarning(
                "⚠️ Channel ID = 0, chưa được cấu hình. Bỏ qua.");
            return;
        }

        var channel = _client.GetChannel(channelId) as IMessageChannel;

        // Guard: không tìm thấy channel
        if (channel == null)
        {
            _logger.LogWarning(
                "❌ Không tìm thấy channel ID: {ChannelId}. " +
                "Kiểm tra: bot có trong server? ID đúng chưa? " +
                "Bot có quyền View Channel + Send Messages không?",
                channelId);
            return;
        }

        await channel.SendMessageAsync(
            text: string.IsNullOrEmpty(message) ? null : message,
            embed: embed,
            components: components,
            allowedMentions: allowedMentions);

        _logger.LogInformation(
            "✅ Sent to channel {ChannelId}",
            channelId);
    }

    // =========================================================
    // DISCORD LOG
    // =========================================================

    private Task OnDiscordLogAsync(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Error:
            case LogSeverity.Critical:
                _logger.LogError(msg.Exception,
                    "[Discord] {Message}", msg.Message);
                break;

            case LogSeverity.Warning:
                _logger.LogWarning(msg.Exception,
                    "[Discord] {Message}", msg.Message);
                break;

            default:
                _logger.LogInformation(
                    "[Discord] {Message}", msg.Message);
                break;
        }

        return Task.CompletedTask;
    }
}