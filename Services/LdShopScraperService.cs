using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class LdShopScraperService : IAsyncDisposable
{
    private readonly ILogger<LdShopScraperService> _logger;
    private const string ShopUrl = "https://www.ldshop.gg/vn";

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LdShopScraperService(ILogger<LdShopScraperService> logger)
    {
        _logger = logger;
    }

    public async Task<List<LdShopPromo>> ScrapePromosAsync()
    {
        try
        {
            _logger.LogInformation("🌐 Scraping {Url} (Playwright)...", ShopUrl);

            var browser = await GetBrowserAsync();
            var page = await browser.NewPageAsync();

            try
            {
                // Load thay vì NetworkIdle — tránh timeout trên SPA
                await page.GotoAsync(ShopUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 30_000
                });

                // Đợi 4s cho JS render xong
                await page.WaitForTimeoutAsync(4000);

                var html = await page.ContentAsync();

                // Dump ra file để debug — path tuyệt đối
                var dumpPath = Path.Combine(AppContext.BaseDirectory, "ldshop_debug.html");
                await File.WriteAllTextAsync(dumpPath, html);
                _logger.LogInformation("📄 HTML dumped ({Length} chars) → {Path}",
                    html.Length, dumpPath);

                var result = ParsePromos(html);
                _logger.LogInformation("✅ Scrape xong: {Count} game có discount", result.Count);
                return result;
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Scrape ldshop thất bại");
            return new List<LdShopPromo>();
        }
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_browser != null) return _browser;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage"
                }
            });

            _logger.LogInformation("🌍 Chromium browser started");
            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<LdShopPromo> ParsePromos(string html)
    {
        var result = new List<LdShopPromo>();

        var linkPattern = new System.Text.RegularExpressions.Regex(
            @"href=""(/vn/(?:top-up|card)/[^""]+)""[^>]*>(.*?)</a>",
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var discountPattern = new System.Text.RegularExpressions.Regex(
            @"(\d+)%OFF",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in linkPattern.Matches(html))
        {
            var href = match.Groups[1].Value;
            var rawText = System.Text.RegularExpressions.Regex.Replace(
                match.Groups[2].Value, @"<[^>]+>", " ").Trim();
            rawText = System.Net.WebUtility.HtmlDecode(rawText);

            var discountMatch = discountPattern.Match(rawText);
            if (!discountMatch.Success) continue;
            var discount = int.Parse(discountMatch.Groups[1].Value);

            var name = discountPattern.Replace(rawText, "").Trim();
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            name = DeduplicateName(name);
            if (result.Any(r => r.Name == name)) continue;

            result.Add(new LdShopPromo
            {
                Name = name,
                Url = "https://www.ldshop.gg" + href,
                DiscountPercent = discount,
                Category = href.Contains("/vn/card/") ? "card" : "top-up"
            });
        }

        return result;
    }

    private static string DeduplicateName(string name)
    {
        var words = name.Split(' ');
        var half = words.Length / 2;
        if (half < 1) return name;

        var firstHalf = string.Join(" ", words.Take(half));
        var secondHalf = string.Join(" ", words.Skip(half));

        return firstHalf.Equals(secondHalf, StringComparison.OrdinalIgnoreCase)
            ? firstHalf : name;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}