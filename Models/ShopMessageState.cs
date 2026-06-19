namespace YouTubeDiscordBot.Models;

public class ShopMessageState
{
    /// <summary>Message ID của embed section LDShop.</summary>
    public ulong LdShopMessageId { get; set; }

    /// <summary>Message ID của embed section Lootbar.</summary>
    public ulong LootbarMessageId { get; set; }

    // ── Legacy fields — giữ để không lỗi khi load file state cũ ─────────────
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ulong PinnedMessageId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, ulong> GameMessageIds { get; set; } = new();
}
