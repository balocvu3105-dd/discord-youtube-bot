using Discord;
using Discord.Interactions;
using Discord.WebSocket;
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

    public ShopCommandModule(
        IShopService shopService,
        IShopMessagePersistenceService persistence,
        IDiscordService discord,
        IOptions<BotConfiguration> config)
    {
        _shopService = shopService;
        _persistence = persistence;
        _discord = discord;
        _config = config.Value;
    }

    [SlashCommand("refreshshop", "Tạo lại toàn bộ shop embeds ngay lập tức")]
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

            // Warm discount cache trước
            await _shopService.WarmDiscountCacheAsync();

            var state = await _persistence.LoadAsync();

            // Reset state để force tạo mới (xóa message ID cũ)
            state.PinnedMessageId = 0;
            state.GameMessageIds.Clear();

            // Tạo lại overview
            var (overviewEmbed, overviewComponents) = await _shopService.BuildOverviewAsync();
            var overviewMsg = await channel.SendMessageAsync(
                embed: overviewEmbed,
                components: overviewComponents);
            state.PinnedMessageId = overviewMsg.Id;

            // FIX: delay nhất quán với ShopBackgroundService
            await Task.Delay(1500);

            // Tạo lại từng game embed
            foreach (var game in _config.ShopGames)
            {
                var result = await _shopService.BuildGameEmbedAsync(game);
                if (result is null) continue;

                var (embed, components) = result.Value;
                var msg = await channel.SendMessageAsync(
                    embed: embed,
                    components: components);
                state.GameMessageIds[game.Name] = msg.Id;

                // FIX: Thêm delay giữa các game embeds để tránh Discord rate-limit
                await Task.Delay(2500);
            }

            await _persistence.SaveAsync(state);

            await FollowupAsync("✅ Shop đã được tạo lại thành công!", ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Lỗi: {ex.Message}", ephemeral: true);
        }
    }
}