using Discord.Interactions;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

public class ShopCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IShopService _shopService;

    public ShopCommandModule(IShopService shopService)
    {
        _shopService = shopService;
    }

    [SlashCommand("shop", "Xem danh sách sản phẩm trong shop")]
    public async Task ShopAsync()
    {
        await DeferAsync();
        // TODO: logic của bạn ở đây
        await FollowupAsync("Shop command!");
    }
}