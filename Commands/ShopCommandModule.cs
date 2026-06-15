using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

public class ShopCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IShopService _shopService;
    private readonly IShopMessagePersistenceService _persistence;
    private readonly IDiscordService _discord;
    private readonly BotConfiguration _config;
    private readonly ILogger<ShopCommandModule> _logger;

    public ShopCommandModule(
        IShopService shopService,
        IShopMessagePersistenceService persistence,
        IDiscordService discord,
        IOptions<BotConfiguration> config,
        ILogger<ShopCommandModule> logger)
    {
        _shopService = shopService;
        _persistence = persistence;
        _discord = discord;
        _config = config.Value;
        _logger = logger;
    }

    [SlashCommand("refreshshop", "Tạo lại toàn bộ shop embed ngay lập tức")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task RefreshShopAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel channel)
            {
                await FollowupAsync("❌ Không tìm thấy shop channel!", ephemeral: true);
                return;
            }

            await _shopService.WarmDiscountCacheAsync();

            // Reset state để force tạo toàn bộ message mới
            var state = new ShopMessageState();

            // Overview
            var (embed, components) = await _shopService.BuildOverviewAsync();
            var msg = await channel.SendMessageAsync(embed: embed, components: components);
            state.PinnedMessageId = msg.Id;
            _logger.LogInformation("[/refreshshop] Overview created — {MessageId}", msg.Id);

            // Game embeds — tạo lại tất cả
            foreach (var game in _config.ShopGames)
            {
                var result = await _shopService.BuildGameEmbedAsync(game);
                if (result is null) continue;

                var (gameEmbed, gameComponents) = result.Value;
                var gameMsg = await channel.SendMessageAsync(embed: gameEmbed, components: gameComponents);
                state.GameMessageIds[game.Name] = gameMsg.Id;
                _logger.LogInformation("[/refreshshop] [{Game}] embed created — {MessageId}", game.Name, gameMsg.Id);

                await Task.Delay(500); // tránh rate limit Discord
            }

            await _persistence.SaveAsync(state);

            await FollowupAsync(
                $"✅ Shop đã được tạo lại thành công! ({_config.ShopGames.Count} game embeds)",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[/refreshshop] thất bại");
            await FollowupAsync($"❌ Lỗi: {ex.Message}", ephemeral: true);
        }
    }
}