using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class DiscordService : IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly BotConfiguration _config;
    private readonly ILogger<DiscordService> _logger;
    private readonly TaskCompletionSource _readyTaskSource = new();

    public DiscordService(
        IOptions<BotConfiguration> config,
        ILogger<DiscordService> logger)
    {
        _config = config.Value;
        _logger = logger;

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds
        };

        _client = new DiscordSocketClient(socketConfig);

        _client.Log += OnLog;
        _client.Ready += OnReady;
    }

    // =========================================================
    // CONNECT
    // =========================================================

    public async Task ConnectAsync()
    {
        _logger.LogInformation(
            "Đang kết nối vào Discord...");

        _logger.LogInformation(
            "Token loaded: {Status}",
            string.IsNullOrWhiteSpace(_config.DiscordToken)
                ? "NULL ❌"
                : "OK ✅");

        try
        {
            await _client.LoginAsync(
                TokenType.Bot,
                _config.DiscordToken);

            await _client.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "❌ Discord login failed");

            throw;
        }

        var completed = await Task.WhenAny(
            _readyTaskSource.Task,
            Task.Delay(TimeSpan.FromSeconds(15)));

        if (completed != _readyTaskSource.Task)
        {
            _logger.LogWarning(
                "Discord connect timeout (có thể token sai)");
        }

        _logger.LogInformation(
            "Bot Discord đã kết nối (hoặc timeout).");
    }

    public DiscordSocketClient Client => _client;

    // =========================================================
    // VIDEO NOTIFICATION
    // =========================================================

    public async Task SendVideoNotificationAsync(
        VideoInfo video)
    {
        var embed = BuildVideoEmbed(video);

        await SendToChannelAsync(
            _config.ChannelName,
            embed);
    }

    // =========================================================
    // PROMO
    // =========================================================

    public async Task SendPromoAsync(
        Embed embed,
        MessageComponent components)
    {
        await SendToChannelAsync(
            _config.PromoChannelName,
            embed,
            components);
    }

    // =========================================================
    // GENERIC SEND
    // =========================================================

    public async Task SendToChannelAsync(
        string channelName,
        Embed embed,
        MessageComponent? components = null)
    {
        var tasks = new List<Task>();

        foreach (var guild in _client.Guilds)
        {
            _logger.LogInformation(
                "📂 Guild: {Guild}",
                guild.Name);

            // Chỉ tìm text/news channel
            var channel = guild.Channels
                .Where(c =>
                    c is SocketTextChannel ||
                    c is SocketNewsChannel)
                .FirstOrDefault(c =>
                    c.Name.Trim().Equals(
                        channelName.Trim(),
                        StringComparison.OrdinalIgnoreCase));

            if (channel == null)
            {
                _logger.LogWarning(
                    "❌ Guild {Guild} không có channel #{Channel}",
                    guild.Name,
                    channelName);

                continue;
            }

            _logger.LogInformation(
                "✅ Found target channel: #{Channel}",
                channel.Name);

            tasks.Add(
                SendSafeAsync(
                    (ISocketMessageChannel)channel,
                    embed,
                    components,
                    guild.Name,
                    channel.Name));
        }

        await Task.WhenAll(tasks);
    }

    // =========================================================
    // SAFE SEND
    // =========================================================

    private async Task SendSafeAsync(
        ISocketMessageChannel channel,
        Embed embed,
        MessageComponent? components,
        string guildName,
        string channelName)
    {
        try
        {
            await channel.SendMessageAsync(
                embed: embed,
                components: components);

            _logger.LogInformation(
                "✅ Sent → {Guild} / #{Channel}",
                guildName,
                channelName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Send failed → {Guild} / #{Channel}",
                guildName,
                channelName);
        }
    }

    // =========================================================
    // EMBED
    // =========================================================

    private static Embed BuildVideoEmbed(
        VideoInfo video)
    {
        var color = video.IsLivestream
            ? Color.Red
            : Color.Blue;

        string headerText = video.IsLivestream
            ? $"🔴 **{video.ChannelName}** đang LIVE!"
            : $"📹 **{video.ChannelName}** vừa đăng video mới!";

        return new EmbedBuilder()
            .WithTitle(video.Title)
            .WithUrl(video.Url)
            .WithDescription(headerText)
            .WithColor(color)
            .WithThumbnailUrl(video.ThumbnailUrl)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("YouTube Notifier Bot")
            .AddField(
                video.IsLivestream
                    ? "🔴 Link Stream"
                    : "▶️ Xem Ngay",
                video.Url)
            .Build();
    }

    // =========================================================
    // DISCORD EVENTS
    // =========================================================

    private Task OnLog(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(
            level,
            msg.Exception,
            "[Discord] {Message}",
            msg.Message);

        return Task.CompletedTask;
    }

    private Task OnReady()
    {
        _logger.LogInformation(
            "Đã đăng nhập với tên: {Username}",
            _client.CurrentUser.Username);

        _readyTaskSource.TrySetResult();

        return Task.CompletedTask;
    }

    // =========================================================
    // DISPOSE
    // =========================================================

    public async ValueTask DisposeAsync()
    {
        await _client.LogoutAsync();

        await _client.StopAsync();

        _client.Dispose();
    }
}