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

    public DiscordService(IOptions<BotConfiguration> config, ILogger<DiscordService> logger)
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

    public async Task ConnectAsync()
    {
        _logger.LogInformation("Đang kết nối vào Discord...");
        await _client.LoginAsync(TokenType.Bot, _config.DiscordToken);
        await _client.StartAsync();
        await _readyTaskSource.Task;
        _logger.LogInformation("Bot Discord đã kết nối và sẵn sàng!");
    }

    // 🔥 GỬI CHO MỌI SERVER
    public async Task SendVideoNotificationAsync(VideoInfo video)
    {
        var embed = BuildEmbed(video);

        foreach (var guild in _client.Guilds)
        {
            var channel = guild.TextChannels
                .FirstOrDefault(c => c.Name.Equals(_config.ChannelName, StringComparison.OrdinalIgnoreCase));

            if (channel == null)
            {
                _logger.LogWarning("Server {Guild} không có channel {ChannelName}",
                    guild.Name, _config.ChannelName);
                continue;
            }

            await channel.SendMessageAsync(embed: embed);

            _logger.LogInformation("Đã gửi thông báo tới {Guild} / #{Channel}",
                guild.Name, channel.Name);
        }
    }

    private Embed BuildEmbed(VideoInfo video)
    {
        var color = video.IsLivestream ? Color.Red : Color.Blue;
        string headerText = video.IsLivestream
            ? $"🔴 **{video.ChannelName}** đang LIVE!"
            : $"📹 **{video.ChannelName}** vừa đăng video mới!";

        var builder = new EmbedBuilder()
            .WithTitle(video.Title)
            .WithUrl(video.Url)
            .WithDescription(headerText)
            .WithColor(color)
            .WithThumbnailUrl(video.ThumbnailUrl)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("YouTube Notifier Bot");

        builder.AddField(video.IsLivestream ? "🔴 Link Stream" : "▶️ Xem Ngay", video.Url);

        return builder.Build();
    }

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
        _logger.Log(level, msg.Exception, "[Discord] {Message}", msg.Message);
        return Task.CompletedTask;
    }

    private Task OnReady()
    {
        _logger.LogInformation("Đã đăng nhập với tên: {Username}", _client.CurrentUser.Username);
        _readyTaskSource.TrySetResult();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _client.LogoutAsync();
        await _client.StopAsync();
        _client.Dispose();
    }
}