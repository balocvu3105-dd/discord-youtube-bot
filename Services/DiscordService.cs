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
            _logger.LogInformation(
                "Đang kết nối vào Discord...");

            if (string.IsNullOrWhiteSpace(
                    _config.DiscordToken))
            {
                throw new Exception(
                    "DiscordToken bị trống!");
            }

            _logger.LogInformation(
                "Token loaded: OK ✅");

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
                    "Đã đăng nhập với tên: {Name}",
                    _client.CurrentUser.Username);

                return Task.CompletedTask;
            };

            await Task.WhenAny(
                readyTask.Task,
                Task.Delay(TimeSpan.FromSeconds(30)));

            if (!readyTask.Task.IsCompleted)
            {
                _logger.LogWarning(
                    "Discord connect timeout (có thể token sai)");
            }

            _logger.LogInformation(
                "Bot Discord đã kết nối (hoặc timeout).");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Không thể kết nối Discord");
        }
    }

    // =========================================================
    // SEND VIDEO NOTIFICATION
    // =========================================================

    public async Task SendVideoNotificationAsync(
        VideoInfo video)
    {
        try
        {
            string url =
                $"https://www.youtube.com/watch?v={video.VideoId}";

            // =====================================================
            // LIVESTREAM
            // =====================================================

            if (video.LiveBroadcastContent == "live")
            {
                string liveMessage =
                    "@everyone\n\n" +
                    "🔴 Tôi đang live rồi anh em ơi:\n\n" +
                    url;

                await SendToChannelAsync(
                    _config.LiveChannelName,
                    liveMessage,
                    allowedMentions: AllowedMentions.All);

                _logger.LogInformation(
                    "✅ Live notification sent: {Title}",
                    video.Title);

                return;
            }

            // =====================================================
            // NORMAL VIDEO
            // =====================================================

            string videoMessage =
                "@everyone\n\n" +
                "📺 Video mới lên sóng:\n\n" +
                url;

            await SendToChannelAsync(
                _config.VideoChannelName,
                videoMessage,
                allowedMentions: AllowedMentions.All);

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
    // SEND PROMO
    // =========================================================

    public async Task SendPromoAsync(
        Embed embed,
        MessageComponent? components = null)
    {
        try
        {
            await SendToChannelAsync(
                _config.PromoChannelName,
                string.Empty,
                embed,
                components);

            _logger.LogInformation(
                "✅ Promo sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ SendPromoAsync failed");
        }
    }

    // =========================================================
    // SEND SAFE
    // =========================================================

    public async Task SendSafeAsync(
        ISocketMessageChannel channel,
        string message,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        try
        {
            await channel.SendMessageAsync(
                text: message,
                embed: embed,
                components: components,
                allowedMentions: allowedMentions);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ SendSafeAsync failed");
        }
    }

    // =========================================================
    // SEND TO CHANNEL
    // =========================================================

    public async Task SendToChannelAsync(
        string channelName,
        string message,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        foreach (var guild in _client.Guilds)
        {
            _logger.LogInformation(
                "📂 Guild: {Guild}",
                guild.Name);

            var channel =
                guild.TextChannels.FirstOrDefault(
                    c => c.Name.Equals(
                        channelName,
                        StringComparison.OrdinalIgnoreCase));

            if (channel == null)
            {
                _logger.LogWarning(
                    "Guild {Guild} không có channel #{Channel}",
                    guild.Name,
                    channelName);

                continue;
            }

            _logger.LogInformation(
                "✅ Found target channel: #{Channel}",
                channel.Name);

            await SendSafeAsync(
                channel,
                message,
                embed,
                components,
                allowedMentions);

            _logger.LogInformation(
                "✅ Sent → {Guild} / #{Channel}",
                guild.Name,
                channel.Name);
        }
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