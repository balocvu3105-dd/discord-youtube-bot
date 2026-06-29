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

        // ── Rate limit (atomic check-and-set) ────────────────────────────────
        // FIX: check + set trong cùng 1 lock → tránh TOCTOU race (2 request đồng thời
        // cùng pass check trước khi cái nào kịp set _lastRefreshUtc).
        var cooldownMessage = TryAcquireOrGetCooldownMessage();
        if (cooldownMessage is not null)
        {
            await FollowupAsync(cooldownMessage, ephemeral: true);
            return;
        }

        // ── Channel guard ─────────────────────────────────────────────────────
        if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel)
        {
            lock (_refreshLock) { _lastRefreshUtc = DateTime.MinValue; } // release cooldown
            await FollowupAsync("❌ Không tìm thấy shop channel!", ephemeral: true);
            return;
        }

        try
        {
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

    /// <summary>
    /// Atomic check-and-set: nếu hết cooldown thì set _lastRefreshUtc ngay trong lock
    /// và trả null (cho phép chạy). Nếu còn cooldown trả về thông báo chờ.
    /// </summary>
    private static string? TryAcquireOrGetCooldownMessage()
    {
        lock (_refreshLock)
        {
            var elapsed = DateTime.UtcNow - _lastRefreshUtc;
            if (elapsed >= RefreshCooldown)
            {
                _lastRefreshUtc = DateTime.UtcNow; // set ngay trong lock
                return null;
            }
            var remaining = (int)(RefreshCooldown - elapsed).TotalSeconds + 1;
            return $"⏳ Vui lòng chờ thêm **{remaining}s** trước khi refresh lại.";
        }
    }
}
