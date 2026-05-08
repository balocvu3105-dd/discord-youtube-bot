using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

public class PromoCommand : InteractionModuleBase<SocketInteractionContext>
{
    private readonly PromoService _promoService;
    private readonly DiscordService _discordService;
    private readonly ILogger<PromoCommand> _logger;

    public PromoCommand(PromoService promoService, DiscordService discordService, ILogger<PromoCommand> logger)
    {
        _promoService = promoService;
        _discordService = discordService;
        _logger = logger;
    }

    // ── /promo ────────────────────────────────────────────────────────────────
    // Chỉ admin mới dùng được
    [SlashCommand("promo", "Gửi khuyến mãi nạp game vào channel")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task SendPromoAsync(
        [Summary("game", "Chọn game muốn post promo")]
        [Autocomplete(typeof(GameNameAutocomplete))]
        string gameName)
    {
        // Defer: báo Discord "đang xử lý" để không bị timeout 3 giây
        await DeferAsync(ephemeral: true);

        var result = _promoService.BuildPromoForGame(gameName);

        if (result == null)
        {
            await FollowupAsync($"❌ Không tìm thấy game: **{gameName}**", ephemeral: true);
            return;
        }

        var (embed, components) = result.Value;
        await _discordService.SendPromoAsync(embed, components);

        _logger.LogInformation("✅ /promo used by {User} → {Game}",
            Context.User.Username, gameName);

        await FollowupAsync($"✅ Đã gửi promo **{gameName}** vào channel!", ephemeral: true);
    }
}

// ── AUTOCOMPLETE ──────────────────────────────────────────────────────────────
// Khi admin gõ /promo, Discord tự gợi ý tên game từ danh sách config
public class GameNameAutocomplete : AutocompleteHandler
{
    private readonly PromoService _promoService;

    public GameNameAutocomplete(PromoService promoService)
    {
        _promoService = promoService;
    }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

        var suggestions = _promoService.GetGameNames()
            .Where(name => name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(name => new AutocompleteResult(name, name))
            .Take(25); // Discord giới hạn tối đa 25 gợi ý

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}