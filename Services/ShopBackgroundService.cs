using Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopBackgroundService : BackgroundService
{
    private readonly DiscordService _discordService;

    private readonly BotConfiguration _config;

    private readonly ShopService _shopService;

    private readonly ShopMessagePersistenceService
        _persistence;

    private readonly ILogger<ShopBackgroundService>
        _logger;

    public ShopBackgroundService(
        DiscordService discordService,
        IOptions<BotConfiguration> config,
        ShopService shopService,
        ShopMessagePersistenceService persistence,
        ILogger<ShopBackgroundService> logger)
    {
        _discordService = discordService;

        _config = config.Value;

        _shopService = shopService;

        _persistence = persistence;

        _logger = logger;
    }

    // =====================================================
    // EXECUTE
    // =====================================================

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "🛒 ShopBackgroundService started. Channel={ChannelId}, Refresh={Hours}h",
            _config.ShopChannelId,
            _config.ShopRefreshHours);

        // Đợi Discord connected
        while (_discordService.Client.ConnectionState
               != Discord.ConnectionState.Connected)
        {
            _logger.LogInformation(
                "⏳ Waiting for Discord connection...");

            await Task.Delay(
                3000,
                stoppingToken);
        }

        _logger.LogInformation(
            "✅ Discord ready — ShopBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshShopAsync(
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Error while refreshing shop");
            }

            var delay =
                TimeSpan.FromHours(
                    _config.ShopRefreshHours);

            _logger.LogInformation(
                "⏱ Next shop refresh in {Hours}h",
                _config.ShopRefreshHours);

            await Task.Delay(
                delay,
                stoppingToken);
        }
    }

    // =====================================================
    // REFRESH SHOP
    // =====================================================

    private async Task RefreshShopAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "🔄 Refreshing shop messages...");

        var channel =
            _discordService.Client.GetChannel(
                _config.ShopChannelId)
            as IMessageChannel;

        if (channel is null)
        {
            _logger.LogWarning(
                "⚠️ Shop channel not found: {ChannelId}",
                _config.ShopChannelId);

            return;
        }

        // =====================================================
        // OVERVIEW
        // =====================================================

        await RefreshOverviewAsync(
            channel);

        // Delay chống rate limit
        await Task.Delay(
            1500,
            cancellationToken);

        // =====================================================
        // GAME EMBEDS
        // =====================================================

        foreach (var game in _config.ShopGames)
        {
            await RefreshGameEmbedAsync(
                channel,
                game);

            // Delay chống rate limit
            await Task.Delay(
                1500,
                cancellationToken);
        }

        _logger.LogInformation(
            "✅ Shop refresh completed");
    }

    // =====================================================
    // OVERVIEW MESSAGE
    // =====================================================

    private async Task RefreshOverviewAsync(
        IMessageChannel channel)
    {
        var state =
            await _persistence.LoadAsync();

        var (embed, components) =
            _shopService.BuildOverview();

        IUserMessage? message = null;

        if (state.PinnedMessageId != 0)
        {
            try
            {
                message =
                    await channel.GetMessageAsync(
                        state.PinnedMessageId)
                    as IUserMessage;
            }
            catch
            {
                // ignored
            }
        }

        if (message is null)
        {
            message =
                await channel.SendMessageAsync(
                    embed: embed,
                    components: components);

            state.PinnedMessageId =
                message.Id;

            await _persistence.SaveAsync(
                state);

            _logger.LogInformation(
                "📌 Created overview message {MessageId}",
                message.Id);
        }
        else
        {
            await message.ModifyAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = components;
            });

            _logger.LogInformation(
                "✏️ Edited overview message {MessageId}",
                message.Id);
        }
    }

    // =====================================================
    // GAME EMBED
    // =====================================================

    private async Task RefreshGameEmbedAsync(
        IMessageChannel channel,
        ShopGameConfig game)
    {
        var state =
            await _persistence.LoadAsync();

        var result =
            _shopService.BuildGameEmbed(game);

        if (result is null)
        {
            return;
        }

        var (embed, components) =
            result.Value;

        IUserMessage? message = null;

        if (state.GameMessageIds.TryGetValue(
                game.Name,
                out var existingMessageId))
        {
            try
            {
                message =
                    await channel.GetMessageAsync(
                        existingMessageId)
                    as IUserMessage;
            }
            catch
            {
                // ignored
            }
        }

        if (message is null)
        {
            message =
                await channel.SendMessageAsync(
                    embed: embed,
                    components: components);

            state.GameMessageIds[game.Name] =
                message.Id;

            await _persistence.SaveAsync(
                state);

            _logger.LogInformation(
                "📌 Created [{Game}] message {MessageId}",
                game.Name,
                message.Id);
        }
        else
        {
            await message.ModifyAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = components;
            });

            _logger.LogInformation(
                "✏️ Edited [{Game}] message {MessageId}",
                game.Name,
                message.Id);
        }
    }

}