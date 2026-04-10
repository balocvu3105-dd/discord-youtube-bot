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
        await _client.LoginAsync(TokenType.Bot, _config.BotToken);
        await _client.StartAsync();
        await _readyTaskSource.Task;
        _logger.LogInformation("Bot Discord đã kết nối và sẵn sàng!");
    }

    public async Task SendVideoNotificationAsync(VideoInfo video)
    {
        if (_client.GetChannel(_config.ChannelId) is not IMessageChannel channel)
        {
            _logger.LogError("Không tìm thấy kênh Discord với ID {ChannelId}.", _config.ChannelId);
            return;
        }

        var embed = BuildEmbed(video);
        await channel.SendMessageAsync(embed: embed);
        _logger.LogInformation("Đã gửi thông báo Discord cho video: {Title}", video.Title);
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

        if (video.IsLivestream)
            builder.AddField("🔴 Link Stream", video.Url);
        else
            builder.AddField("▶️ Xem Ngay", video.Url);

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