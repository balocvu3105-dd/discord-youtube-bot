using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Lưu/đọc ShopMessageState — message IDs của các shop embed.
///
/// FIX so với code cũ:
///   - Load 1 lần duy nhất per refresh cycle (không load lại mỗi game)
///   - Thread-safe qua AsyncJsonStore
/// </summary>
public class ShopMessagePersistenceService
    : AsyncJsonStore<ShopMessageState>, IShopMessagePersistenceService
{
    private const string FilePath_ = "data/shop_messages.json";
    private readonly ILogger<ShopMessagePersistenceService> _logger;

    protected override string FilePath => FilePath_;
    protected override ILogger Logger => _logger;

    public ShopMessagePersistenceService(
        ILogger<ShopMessagePersistenceService> logger)
    {
        _logger = logger;
    }

    public async Task<ShopMessageState> LoadAsync()
    {
        var state = await ReadAsync();
        _logger.LogInformation(
            "ShopMessageState loaded — PinnedId={Id} GameCount={Count}",
            state.PinnedMessageId, state.GameMessageIds.Count);
        return state;
    }

    public async Task SaveAsync(ShopMessageState state)
    {
        await WriteAsync(state);
        _logger.LogInformation(
            "ShopMessageState saved — PinnedId={Id} GameCount={Count}",
            state.PinnedMessageId, state.GameMessageIds.Count);
    }
}