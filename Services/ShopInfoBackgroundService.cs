using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

public class ShopInfoBackgroundService : BackgroundService
{
    private readonly ShopInfoService _shopInfoService;
    private readonly DiscordService _discordService;
    private readonly BotConfiguration _config;
    private readonly ILogger<ShopInfoBackgroundService> _logger;

    // Lưu MessageId của tin nhắn hiện tại trong mỗi guild
    // Key = GuildId, Value = MessageId
    private readonly Dictionary<ulong, ulong> _postedMessageIds = new();

    public ShopInfoBackgroundService(
        ShopInfoService shopInfoService,
        DiscordService discordService,
        IOptions<BotConfiguration> config,
        ILogger<ShopInfoBackgroundService> logger)
    {
        _shopInfoService = shopInfoService;
        _discordService = discordService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "🛒 ShopInfoBackgroundService started. Refresh every {Hours}h to #{Channel}",
            _config.ShopInfoRefreshHours,
            _config.ShopInfoChannelName);

        // Đợi bot Discord Ready trước
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshShopInfoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ShopInfoBackgroundService error");
            }

            await Task.Delay(
                TimeSpan.FromHours(_config.ShopInfoRefreshHours),
                stoppingToken);
        }
    }

    private async Task RefreshShopInfoAsync()
    {
        var (embed, components) = _shopInfoService.BuildShopOverview();

        // Lấy client Discord từ DiscordService
        var client = _discordService.Client;

        foreach (var guild in client.Guilds)
        {
            // Tìm channel #thong-tin-shop
            var channel = guild.TextChannels
                .FirstOrDefault(c => c.Name.Equals(
                    _config.ShopInfoChannelName,
                    StringComparison.OrdinalIgnoreCase));

            if (channel == null)
            {
                _logger.LogWarning(
                    "Guild {Guild} không có channel #{Channel}",
                    guild.Name, _config.ShopInfoChannelName);
                continue;
            }

            // Xóa tin nhắn cũ nếu có
            if (_postedMessageIds.TryGetValue(guild.Id, out var oldMsgId))
            {
                try
                {
                    var oldMsg = await channel.GetMessageAsync(oldMsgId);
                    if (oldMsg != null)
                    {
                        await channel.DeleteMessageAsync(oldMsgId);
                        _logger.LogInformation(
                            "🗑️ Deleted old shop info message in {Guild}", guild.Name);
                    }
                }
                catch (Exception ex)
                {
                    // Không crash nếu xóa thất bại (vd: tin nhắn đã bị xóa tay)
                    _logger.LogWarning(ex,
                        "Could not delete old message in {Guild}", guild.Name);
                }
            }

            // Đăng tin mới và lưu MessageId
            try
            {
                var newMsg = await channel.SendMessageAsync(
                    embed: embed,
                    components: components);

                // Lưu MessageId để lần sau xóa được
                _postedMessageIds[guild.Id] = newMsg.Id;

                _logger.LogInformation(
                    "✅ Shop info posted in {Guild} / #{Channel} (MsgId: {Id})",
                    guild.Name, channel.Name, newMsg.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Failed to post shop info in {Guild}", guild.Name);
            }
        }
    }
}