using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Background;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

[RequireContext(ContextType.Guild)]
public class ShopCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDiscordService _discord;
    private readonly BotConfiguration _config;
    private readonly ShopBackgroundService _shopBackground;
    private readonly ILogger<ShopCommandModule> _logger;

    // ── Cooldown ─────────────────────────────────────────────────────────────
    // static + lock: cooldown toàn cục, thread-safe.
    // ShopCommandModule là Transient nên không dùng instance field.
    private static readonly object _refreshLock = new();
    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(60);

    public ShopCommandModule(
        IDiscordService discord,
        IOptions<BotConfiguration> config,
        ShopBackgroundService shopBackground,
        ILogger<ShopCommandModule> logger)
    {
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

        // ── Rate limit (thread-safe) ──────────────────────────────────────────
        var cooldownMessage = GetCooldownMessage();
        if (cooldownMessage is not null)
        {
            await FollowupAsync(cooldownMessage, ephemeral: true);
            return;
        }

        // ── Channel guard ─────────────────────────────────────────────────────
        if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel)
        {
            await FollowupAsync("❌ Không tìm thấy shop channel!", ephemeral: true);
            return;
        }

        try
        {
            lock (_refreshLock) { _lastRefreshUtc = DateTime.UtcNow; }

            await _shopBackground.RefreshShopAsync();
            await FollowupAsync("✅ Shop đã được cập nhật!", ephemeral: true);
        }
        catch (Exception ex)
        {
            // Reset cooldown khi lỗi để admin thử lại ngay.
            // KHÔNG forward ex.Message — tránh leak thông tin nội bộ ra Discord.
            lock (_refreshLock) { _lastRefreshUtc = DateTime.MinValue; }
            _logger.LogError(ex, "[/refreshshop] thất bại — userId={UserId}", Context.User.Id);
            await FollowupAsync(
                "❌ Đã xảy ra lỗi khi cập nhật shop. Vui lòng thử lại sau.",
                ephemeral: true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <returns>Thông báo cooldown nếu còn hiệu lực, null nếu được phép chạy.</returns>
    private static string? GetCooldownMessage()
    {
        lock (_refreshLock)
        {
            var elapsed = DateTime.UtcNow - _lastRefreshUtc;
            if (elapsed >= RefreshCooldown) return null;
            var remaining = (int)(RefreshCooldown - elapsed).TotalSeconds + 1;
            return $"⏳ Vui lòng chờ thêm **{remaining}s** trước khi refresh lại.";
        }
    }
}
