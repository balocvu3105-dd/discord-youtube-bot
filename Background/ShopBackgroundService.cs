using Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Background;

public class ShopBackgroundService : BackgroundService
{
    private readonly IDiscordService _discord;
    private readonly BotConfiguration _config;
    private readonly IShopService _shopService;
    private readonly IShopMessagePersistenceService _persistence;
    private readonly ILogger<ShopBackgroundService> _logger;

    public ShopBackgroundService(
        IDiscordService discord,
        IOptions<BotConfiguration> config,
        IShopService shopService,
        IShopMessagePersistenceService persistence,
        ILogger<ShopBackgroundService> logger)
    {
        _discord = discord;
        _config = config.Value;
        _shopService = shopService;
        _persistence = persistence;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ShopBackgroundService starting — Channel={ChannelId}, Refresh={Hours}h",
            _config.ShopChannelId, _config.ShopRefreshHours);

        await _discord.WaitForReadyAsync();
        _logger.LogInformation("Discord ready — ShopBackgroundService running");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshShopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ShopBackgroundService — unhandled exception during refresh");
            }

            _logger.LogInformation("Next shop refresh in {Hours}h", _config.ShopRefreshHours);

            try
            {
                await Task.Delay(TimeSpan.FromHours(_config.ShopRefreshHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshShopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Refreshing shop messages...");

        if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel channel)
        {
            _logger.LogWarning("Shop channel không tìm thấy: {ChannelId}", _config.ShopChannelId);
            return;
        }

        await _shopService.WarmDiscountCacheAsync();

        var state = await _persistence.LoadAsync();
        var stateChanged = await RefreshOverviewAsync(channel, state);

        if (stateChanged)
            await _persistence.SaveAsync(state);

        _logger.LogInformation("Shop refresh completed");
    }

    private async Task<bool> RefreshOverviewAsync(IMessageChannel channel, ShopMessageState state)
    {
        var (embed, components) = await _shopService.BuildOverviewAsync();

        IUserMessage? existing = null;
        if (state.PinnedMessageId != 0)
        {
            try { existing = await channel.GetMessageAsync(state.PinnedMessageId) as IUserMessage; }
            catch { /* message bị xóa */ }
        }

        if (existing is null)
        {
            var msg = await channel.SendMessageAsync(embed: embed, components: components);
            state.PinnedMessageId = msg.Id;
            _logger.LogInformation("Overview message created — {MessageId}", msg.Id);
            return true;
        }

        await existing.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        _logger.LogInformation("Overview message updated — {MessageId}", existing.Id);
        return false;
    }
}