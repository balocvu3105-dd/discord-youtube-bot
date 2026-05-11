using Discord;
using Discord.Interactions;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

// Module chứa slash commands và button handlers liên quan đến shop
public class ShopCommand : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ShopInfoService _shopInfoService;

    // Dependency Injection — Discord.Net tự inject
    public ShopCommand(ShopInfoService shopInfoService)
    {
        _shopInfoService = shopInfoService;
    }

    // ── SLASH COMMAND: /shop ──────────────────────────────────────────────
    // Admin dùng để đăng lại bảng thông tin shop thủ công
    [SlashCommand("shop", "Đăng bảng thông tin shop (admin only)")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task ShopCommand_Async()
    {
        await DeferAsync(ephemeral: true); // "Bot đang xử lý..."

        try
        {
            var (embed, components) = _shopInfoService.BuildShopOverview();

            // Đăng vào channel hiện tại (admin tự chọn đúng channel)
            await Context.Channel.SendMessageAsync(
                embed: embed,
                components: components);

            await FollowupAsync(
                "✅ Đã đăng bảng thông tin shop!",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync(
                $"❌ Lỗi: {ex.Message}",
                ephemeral: true);
        }
    }

    // ── BUTTON HANDLER: shop_game:<GameName> ─────────────────────────────
    // Được gọi khi user bấm vào button của 1 game cụ thể
    //
    // [ComponentInteraction("shop_game:*")] nghĩa là:
    // - Bắt tất cả interaction có CustomId bắt đầu bằng "shop_game:"
    // - Phần sau dấu ":" được truyền vào tham số "gameName"
    [ComponentInteraction("shop_game:*")]
    public async Task OnGameButtonClick(string gameName)
    {
        // DeferAsync ephemeral: true = chỉ user này thấy "Bot đang xử lý..."
        await DeferAsync(ephemeral: true);

        var embed = _shopInfoService.BuildGameDetail(gameName);

        if (embed == null)
        {
            await FollowupAsync(
                "❌ Không tìm thấy thông tin game này.",
                ephemeral: true);
            return;
        }

        var components = _shopInfoService.BuildGameDetailComponents(gameName);

        // ephemeral: true = CHỈ USER NÀY THẤY — không ai khác trong channel thấy
        await FollowupAsync(
            embed: embed,
            components: components,
            ephemeral: true);
    }
}