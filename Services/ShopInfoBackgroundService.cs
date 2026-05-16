using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using Discord;

namespace YouTubeDiscordBot.Services;

public class ShopInfoBackgroundService : BackgroundService
{
    private readonly ShopInfoService _shopInfoService;
    private readonly DiscordService _discordService;
    private readonly BotConfiguration _config;
    private readonly ILogger<ShopInfoBackgroundService> _logger;

    // Lưu MessageId của tin nhắn hiện tại để xóa lần sau
    // Key = ChannelId, Value = MessageId
    private ulong _postedMessageId = 0;

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
            "🛒 ShopInfoBackgroundService started. Refresh every {Hours}h to channel ID: {ChannelId}",
            _config.ShopInfoRefreshHours,
            _config.ShopInfoChannelId);

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
        // Guard: chưa cấu hình ID
        if (_config.ShopInfoChannelId == 0)
        {
            _logger.LogWarning(
                "⚠️ ShopInfoChannelId = 0, chưa được cấu hình. Bỏ qua.");
            return;
        }

        // Lấy channel trực tiếp bằng ID
        var channel = _discordService.Client
            .GetChannel(_config.ShopInfoChannelId) as IMessageChannel;

        if (channel == null)
        {
            _logger.LogWarning(
                "❌ Không tìm thấy channel ID: {ChannelId}. " +
                "Kiểm tra: bot có trong server? ID đúng chưa? " +
                "Bot có quyền View Channel + Send Messages không?",
                _config.ShopInfoChannelId);
            return;
        }

        var (embed, components) = _shopInfoService.BuildShopOverview();

        // Xóa tin nhắn cũ nếu có
        if (_postedMessageId != 0)
        {
            try
            {
                var oldMsg = await channel.GetMessageAsync(_postedMessageId);
                if (oldMsg != null)
                {
                    await channel.DeleteMessageAsync(_postedMessageId);
                    _logger.LogInformation("🗑️ Deleted old shop info message");
                }
            }
            catch (Exception ex)
            {
                // Không crash nếu xóa thất bại (tin nhắn đã bị xóa tay)
                _logger.LogWarning(ex, "Could not delete old shop info message");
            }
        }

        // Đăng tin mới và lưu MessageId
        try
        {
            var newMsg = await channel.SendMessageAsync(
                embed: embed,
                components: components);

            _postedMessageId = newMsg.Id;

            _logger.LogInformation(
                "✅ Shop info posted to channel {ChannelId} (MsgId: {MsgId})",
                _config.ShopInfoChannelId,
                newMsg.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to post shop info");
        }
    }
}