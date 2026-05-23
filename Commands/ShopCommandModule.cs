using Discord;
using Discord.Interactions;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

/// <summary>
/// Slash commands liên quan đến shop.
///
/// Để slash command hoạt động, InteractionService phải được setup
/// trong Program.cs (đăng ký commands với Discord và handle interaction events).
/// Code cũ thiếu bước này → /shop không bao giờ hoạt động.
/// </summary>
[DefaultMemberPermissions(GuildPermission.Administrator)]
public class ShopCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IShopService _shopService;
    private readonly IShopMessagePersistenceService _persistence;

    public ShopCommandModule(
        IShopService shopService,
        IShopMessagePersistenceService persistence)
    {
        _shopService = shopService;
        _persistence = persistence;
    }

    // ── /shop ────────────────────────────────────────────────────────────────
    // Đăng bảng shop overview vào channel hiện tại (1 lần, không track message ID)

    [SlashCommand("shop", "Đăng bảng thông tin shop vào channel hiện tại (admin only)")]
    public async Task ShopAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var (embed, components) = _shopService.BuildOverview();
            await Context.Channel.SendMessageAsync(embed: embed, components: components);
            await FollowupAsync("✅ Đã đăng bảng thông tin shop!", ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Lỗi: {ex.Message}", ephemeral: true);
        }
    }

    // ── /refreshshop ─────────────────────────────────────────────────────────
    // Force refresh tất cả shop messages ngay lập tức
    // (không cần đợi đến lịch refresh tự động)

    [SlashCommand("refreshshop", "Force refresh tất cả shop embeds ngay (admin only)")]
    public async Task RefreshShopAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            // Load state hiện tại
            var state = await _persistence.LoadAsync();
            var stateChanged = false;

            // Overview
            var (overviewEmbed, overviewComponents) = _shopService.BuildOverview();

            if (state.PinnedMessageId != 0)
            {
                var existing = await Context.Channel.GetMessageAsync(state.PinnedMessageId)
                    as Discord.IUserMessage;

                if (existing is not null)
                {
                    await existing.ModifyAsync(m =>
                    {
                        m.Embed = overviewEmbed;
                        m.Components = overviewComponents;
                    });
                }
                else
                {
                    var msg = await Context.Channel.SendMessageAsync(
                        embed: overviewEmbed, components: overviewComponents);
                    state.PinnedMessageId = msg.Id;
                    stateChanged = true;
                }
            }

            if (stateChanged)
                await _persistence.SaveAsync(state);

            await FollowupAsync("✅ Đã refresh shop embeds!", ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Lỗi: {ex.Message}", ephemeral: true);
        }
    }
}