using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Interface chuẩn chung cho mọi nền tảng livestream (YouTube, TikTok, Twitch, Kick, Facebook...).
/// Khi muốn thêm một nền tảng mới, chỉ cần implement interface này và đăng ký vào DI.
/// </summary>
public interface IStreamPlatformProvider
{
    /// <summary>Tên nền tảng, e.g. "YouTube", "TikTok", "Twitch", "Kick", "Facebook".</summary>
    string PlatformName { get; }

    /// <summary>Emoji đặc trưng, e.g. "🔴", "🎵", "🟣", "🟢", "🔵".</summary>
    string PlatformEmoji { get; }

    /// <summary>Màu sắc Embed hex, e.g. "FF0000", "00F2FE", "9146FF", "53FC18", "1877F2".</summary>
    string PlatformColorHex { get; }

    /// <summary>
    /// Kiểm tra trạng thái live hiện tại của một kênh/username trên nền tảng.
    /// </summary>
    Task<StreamStatusResult> CheckLiveStatusAsync(string usernameOrId, CancellationToken ct = default);
}
