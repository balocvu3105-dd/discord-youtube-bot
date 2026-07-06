using System.Text;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Commands;

[RequireContext(ContextType.Guild)]
[Group("live", "Quản lý theo dõi livestream đa nền tảng (Twitch, Kick, FB...)")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public class LiveManagementCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly StreamerManagerService _streamerManager;
    private readonly IEnumerable<IStreamPlatformProvider> _providers;
    private readonly ILogger<LiveManagementCommandModule> _logger;

    public LiveManagementCommandModule(
        StreamerManagerService streamerManager,
        IEnumerable<IStreamPlatformProvider> providers,
        ILogger<LiveManagementCommandModule> logger)
    {
        _streamerManager = streamerManager;
        _providers = providers;
        _logger = logger;
    }

    [SlashCommand("status", "Xem danh sách tất cả các kênh đang theo dõi và trạng thái Live hiện tại")]
    public async Task StatusAsync()
    {
        await DeferAsync(ephemeral: true);

        var streamers = await _streamerManager.GetAllTrackedStreamersAsync();
        var state = await _streamerManager.LoadStateAsync();

        if (streamers.Count == 0)
        {
            await FollowupAsync("⚠️ Hiện chưa có kênh livestream nào được theo dõi trong hệ thống.", ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        foreach (var group in streamers.GroupBy(x => x.Platform))
        {
            var provider = _providers.FirstOrDefault(p => string.Equals(p.PlatformName, group.Key, StringComparison.OrdinalIgnoreCase));
            var emoji = provider?.PlatformEmoji ?? "🔴";

            sb.AppendLine($"**{emoji} {group.Key} ({group.Count()} kênh):**");

            foreach (var item in group)
            {
                var isLive = state.LiveNotifiedStates.GetValueOrDefault(item.Key, false);
                var statusIcon = isLive ? "🟢 **ĐANG LIVE**" : "⚫ Offline";
                var displayName = !string.IsNullOrWhiteSpace(item.CustomName) ? item.CustomName : item.UsernameOrId;
                sb.AppendLine($"  • `{item.UsernameOrId}` ({displayName}) — {statusIcon}");
            }
            sb.AppendLine();
        }

        var embed = new EmbedBuilder()
            .WithTitle("📡 Danh sách Kênh Theo dõi Livestream")
            .WithDescription(sb.ToString())
            .WithColor(Color.Blue)
            .WithFooter("Dùng /live add hoặc /live remove để quản lý")
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("add", "Thêm một kênh livestream mới cần theo dõi (Twitch, Kick, Facebook...)")]
    public async Task AddAsync(
        [Summary("platform", "Nền tảng (Twitch, Kick, Facebook)")]
        [Choice("Twitch", "Twitch")]
        [Choice("Kick", "Kick")]
        [Choice("Facebook", "Facebook")]
        string platform,
        [Summary("username", "Username hoặc Slug của kênh trên nền tảng")] string username,
        [Summary("custom_name", "Tên hiển thị tùy chỉnh (e.g. CataWuwa)")] string? customName = null)
    {
        await DeferAsync(ephemeral: true);

        var item = new StreamerConfigItem
        {
            Platform = platform,
            UsernameOrId = username.Trim(),
            CustomName = !string.IsNullOrWhiteSpace(customName) ? customName.Trim() : username.Trim()
        };

        var added = await _streamerManager.AddDynamicStreamerAsync(item);
        if (!added)
        {
            await FollowupAsync($"⚠️ Kênh **{item.UsernameOrId}** trên nền tảng **{platform}** đã tồn tại trong danh sách theo dõi!", ephemeral: true);
            return;
        }

        await FollowupAsync($"✅ Đã thêm thành công kênh **{item.CustomName}** (`{item.UsernameOrId}`) trên nền tảng **{platform}** vào hệ thống theo dõi tự động!", ephemeral: true);
    }

    [SlashCommand("remove", "Xóa một kênh livestream khỏi danh sách theo dõi")]
    public async Task RemoveAsync(
        [Summary("platform", "Nền tảng (Twitch, Kick, Facebook)")]
        [Choice("Twitch", "Twitch")]
        [Choice("Kick", "Kick")]
        [Choice("Facebook", "Facebook")]
        string platform,
        [Summary("username", "Username hoặc Slug của kênh cần xóa")] string username)
    {
        await DeferAsync(ephemeral: true);

        var removed = await _streamerManager.RemoveDynamicStreamerAsync(platform, username.Trim());
        if (!removed)
        {
            await FollowupAsync($"❌ Không tìm thấy kênh **{username}** trên nền tảng **{platform}** trong danh sách thêm động (hoặc kênh này nằm cố định trong appsettings.json).", ephemeral: true);
            return;
        }

        await FollowupAsync($"🗑️ Đã xóa kênh **{username}** (`{platform}`) khỏi danh sách theo dõi tự động.", ephemeral: true);
    }

    [SlashCommand("check", "Kiểm tra ngay lập tức trạng thái live thực tế của một kênh")]
    public async Task CheckAsync(
        [Summary("platform", "Nền tảng (Twitch, Kick, Facebook)")]
        [Choice("Twitch", "Twitch")]
        [Choice("Kick", "Kick")]
        [Choice("Facebook", "Facebook")]
        string platform,
        [Summary("username", "Username hoặc Slug của kênh")] string username)
    {
        await DeferAsync(ephemeral: true);

        var provider = _providers.FirstOrDefault(p => string.Equals(p.PlatformName, platform, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            await FollowupAsync($"❌ Chưa hỗ trợ provider cho nền tảng **{platform}**.", ephemeral: true);
            return;
        }

        try
        {
            var status = await provider.CheckLiveStatusAsync(username.Trim());
            var statusText = status.IsLive ? "🟢 **ĐANG LIVE**" : "⚫ **OFFLINE**";

            var embed = new EmbedBuilder()
                .WithTitle($"{provider.PlatformEmoji} Kiểm tra: {platform} - {username}")
                .WithDescription($"Trạng thái hiện tại: {statusText}\n\n**Tiêu đề:** {status.Title}")
                .WithColor(status.IsLive ? Color.Green : Color.LightGrey)
                .WithUrl(status.StreamUrl)
                .WithCurrentTimestamp();

            if (!string.IsNullOrWhiteSpace(status.ThumbnailUrl))
                embed.WithImageUrl(status.ThumbnailUrl);

            if (status.ViewerCount > 0)
                embed.AddField("👥 Người xem", $"{status.ViewerCount:N0}", inline: true);

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[/live check] Lỗi kiểm tra {Platform} - {User}", platform, username);
            await FollowupAsync($"❌ Lỗi kết nối đến API **{platform}** khi kiểm tra kênh `{username}`: {ex.Message}", ephemeral: true);
        }
    }
}
