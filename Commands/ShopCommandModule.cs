using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Background;
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
    private readonly ShopBackgroundService _shopBackground;
    private readonly ILogger<ShopCommandModule> _logger;

    public ShopCommandModule(
        IShopService shopService,
        IShopMessagePersistenceService persistence,
        IDiscordService discord,
        IOptions<BotConfiguration> config,
        ShopBackgroundService shopBackground,
        ILogger<ShopCommandModule> logger)
    {
        _shopService = shopService;
        _persistence = persistence;
        _discord = discord;
        _config = config.Value;
        _shopBackground = shopBackground;
        _logger = logger;
    }

    [SlashCommand("refreshshop", "Cập nhật lại shop embed (chỉ edit, không tạo message mới)")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task RefreshShopAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel)
            {
                await FollowupAsync("❌ Không tìm thấy shop channel!", ephemeral: true);
                return;
            }

            // Dùng RefreshShopAsync từ BackgroundService để đảm bảo logic nhất quán.
            // ShopBackgroundService là singleton nên có thể inject trực tiếp.
            await _shopBackground.RefreshShopAsync();

            await FollowupAsync("✅ Shop đã được cập nhật!", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[/refreshshop] thất bại");
            await FollowupAsync($"❌ Lỗi: {ex.Message}", ephemeral: true);
        }
    }
}
