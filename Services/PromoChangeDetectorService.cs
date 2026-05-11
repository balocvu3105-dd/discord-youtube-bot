using Microsoft.Extensions.Logging;
using System.Text.Json;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// So sánh promo hiện tại với lần scrape trước.
/// Lưu snapshot vào file JSON để persist qua restart.
/// </summary>
public class PromoChangeDetectorService
{
    private readonly ILogger<PromoChangeDetectorService> _logger;

    // File lưu snapshot lần scrape cuối
    private const string SnapshotPath = "promo_snapshot.json";

    // Cache trong memory (tránh đọc file liên tục)
    private List<LdShopPromo> _lastSnapshot = new();

    public PromoChangeDetectorService(ILogger<PromoChangeDetectorService> logger)
    {
        _logger = logger;
        LoadSnapshot(); // Load từ file khi khởi động
    }

    // ========================= PUBLIC =========================

    /// <summary>
    /// So sánh list mới với snapshot cũ.
    /// Trả về các thay đổi đáng thông báo.
    /// </summary>
    public PromoChanges DetectChanges(List<LdShopPromo> current)
    {
        var changes = new PromoChanges();

        // Game mới xuất hiện (chưa có trong snapshot)
        changes.NewGames = current
            .Where(c => !_lastSnapshot.Any(s => s.Name == c.Name))
            .ToList();

        // Game thay đổi % discount
        changes.UpdatedGames = current
            .Where(c =>
            {
                var old = _lastSnapshot.FirstOrDefault(s => s.Name == c.Name);
                return old != null && old.DiscountPercent != c.DiscountPercent;
            })
            .Select(c => new PromoUpdate
            {
                Promo = c,
                OldDiscount = _lastSnapshot.First(s => s.Name == c.Name).DiscountPercent
            })
            .ToList();

        // Game biến mất khỏi trang (ưu đãi kết thúc)
        changes.RemovedGames = _lastSnapshot
            .Where(s => !current.Any(c => c.Name == s.Name))
            .ToList();

        // Cập nhật snapshot mới (cả khi không có thay đổi)
        SaveSnapshot(current);
        _lastSnapshot = current;

        if (changes.HasChanges)
            _logger.LogInformation(
                "🔔 Phát hiện thay đổi: +{New} mới, ~{Updated} cập nhật, -{Removed} hết hạn",
                changes.NewGames.Count,
                changes.UpdatedGames.Count,
                changes.RemovedGames.Count);
        else
            _logger.LogInformation("✅ Không có thay đổi promo");

        return changes;
    }

    // ========================= SNAPSHOT =========================

    private void LoadSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotPath)) return;
            var json = File.ReadAllText(SnapshotPath);
            _lastSnapshot = JsonSerializer.Deserialize<List<LdShopPromo>>(json)
                            ?? new List<LdShopPromo>();
            _logger.LogInformation("📂 Loaded promo snapshot: {Count} items", _lastSnapshot.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Không load được snapshot, bắt đầu mới");
            _lastSnapshot = new List<LdShopPromo>();
        }
    }

    private void SaveSnapshot(List<LdShopPromo> promos)
    {
        try
        {
            var json = JsonSerializer.Serialize(promos, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SnapshotPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Không lưu được snapshot");
        }
    }
}

// ========================= DATA CLASSES =========================

public class PromoChanges
{
    public List<LdShopPromo> NewGames { get; set; } = new();
    public List<PromoUpdate> UpdatedGames { get; set; } = new();
    public List<LdShopPromo> RemovedGames { get; set; } = new();

    public bool HasChanges =>
        NewGames.Count > 0 ||
        UpdatedGames.Count > 0 ||
        RemovedGames.Count > 0;
}

public class PromoUpdate
{
    public LdShopPromo Promo { get; set; } = new();
    public int OldDiscount { get; set; }
}