using Discord;
using Discord.Interactions;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

// Module chứa slash commands liên quan đến shop
public class ShopCommand : InteractionModuleBase
{
    private readonly ShopService _shopService;

    public ShopCommand(ShopService shopService)
    {
        _shopService = shopService;
    }

    // ── SLASH COMMAND: /shop ──────────────────────────────────────────────
    // Admin dùng để đăng lại bảng thông tin shop thủ công
    [SlashCommand("shop", "Đăng bảng thông tin shop (admin only)")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task ShopCommand_Async()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var (embed, components) =
                _shopService.BuildOverview();

            // Đăng vào channel hiện tại
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

}