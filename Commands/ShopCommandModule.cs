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

    [SlashCommand("refreshshop", "Tạo lại shop embed ngay lập tức")]
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

            var state = await _persistence.LoadAsync();

            // Reset để force tạo message mới
            state.PinnedMessageId = 0;
            state.GameMessageIds.Clear();

            var (embed, components) = await _shopService.BuildOverviewAsync();
            var msg = await channel.SendMessageAsync(embed: embed, components: components);
            state.PinnedMessageId = msg.Id;

            await _persistence.SaveAsync(state);

            await FollowupAsync("✅ Shop đã được tạo lại thành công!", ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Lỗi: {ex.Message}", ephemeral: true);
        }
    }
}