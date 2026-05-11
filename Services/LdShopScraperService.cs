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

    public LdShopScraperService(
        ILogger<LdShopScraperService> logger)
    {
        _logger = logger;
    }

    // =========================================================
    // PUBLIC
    // =========================================================

    public async Task<List<LdShopPromo>> ScrapePromosAsync()
    {
        try
        {
            _logger.LogInformation(
                "🌐 Scraping {Url} (Playwright)...",
                ShopUrl);

            var browser = await GetBrowserAsync();

            var context = await browser.NewContextAsync(
                new BrowserNewContextOptions
                {
                    UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                        "AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/122.0.0.0 Safari/537.36",

                    ViewportSize = new ViewportSize
                    {
                        Width = 1366,
                        Height = 768
                    },

                    Locale = "vi-VN",

                    TimezoneId = "Asia/Ho_Chi_Minh"
                });

            var page = await context.NewPageAsync();

            try
            {
                await page.GotoAsync(
                    ShopUrl,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = 90_000
                    });

                // Chờ JS render
                await page.WaitForTimeoutAsync(10000);

                // Fake user interaction
                await page.Mouse.MoveAsync(200, 300);

                await page.Mouse.WheelAsync(0, 5000);

                await page.WaitForTimeoutAsync(5000);

                var html = await page.ContentAsync();

                // Debug HTML dump
                var dumpPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "ldshop_debug.html");

                await File.WriteAllTextAsync(dumpPath, html);

                _logger.LogInformation(
                    "📄 HTML dumped ({Length} chars) → {Path}",
                    html.Length,
                    dumpPath);

                var result = ParsePromos(html);

                _logger.LogInformation(
                    "✅ Scrape xong: {Count} game có discount",
                    result.Count);

                return result;
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Scrape ldshop thất bại");

            return new List<LdShopPromo>();
        }
    }

    // =========================================================
    // BROWSER
    // =========================================================

    private async Task<IBrowser> GetBrowserAsync()
    {
        await _lock.WaitAsync();

        try
        {
            if (_browser != null)
                return _browser;

            _playwright = await Playwright.CreateAsync();

            _browser = await _playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions
                {
                    Headless = false,

                    SlowMo = 100,

                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--disable-dev-shm-usage",
                        "--no-sandbox",
                        "--disable-setuid-sandbox"
                    }
                });

            _logger.LogInformation(
                "🌍 Chromium browser started");

            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    // =========================================================
    // PARSER
    // =========================================================

    private List<LdShopPromo> ParsePromos(string html)
    {
        var result = new List<LdShopPromo>();

        var linkPattern =
            new System.Text.RegularExpressions.Regex(
                @"href=""(/vn/(?:top-up|card)/[^""]+)""[^>]*>(.*?)</a>",
                System.Text.RegularExpressions.RegexOptions.Singleline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var discountPattern =
            new System.Text.RegularExpressions.Regex(
                @"(\d+)%OFF",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match
                 in linkPattern.Matches(html))
        {
            var href = match.Groups[1].Value;

            var rawText =
                System.Text.RegularExpressions.Regex.Replace(
                    match.Groups[2].Value,
                    @"<[^>]+>",
                    " ")
                .Trim();

            rawText =
                System.Net.WebUtility.HtmlDecode(rawText);

            var discountMatch =
                discountPattern.Match(rawText);

            if (!discountMatch.Success)
                continue;

            var discount =
                int.Parse(discountMatch.Groups[1].Value);

            var name =
                discountPattern.Replace(rawText, "")
                .Trim();

            name =
                System.Text.RegularExpressions.Regex.Replace(
                    name,
                    @"\s+",
                    " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            name = DeduplicateName(name);

            if (result.Any(r => r.Name == name))
                continue;

            result.Add(new LdShopPromo
            {
                Name = name,
                Url = "https://www.ldshop.gg" + href,
                DiscountPercent = discount,
                Category = href.Contains("/vn/card/")
                    ? "card"
                    : "top-up"
            });
        }

        return result;
    }

    // =========================================================
    // HELPER
    // =========================================================

    private static string DeduplicateName(string name)
    {
        var words = name.Split(' ');

        var half = words.Length / 2;

        if (half < 1)
            return name;

        var firstHalf =
            string.Join(" ", words.Take(half));

        var secondHalf =
            string.Join(" ", words.Skip(half));

        return firstHalf.Equals(
            secondHalf,
            StringComparison.OrdinalIgnoreCase)
            ? firstHalf
            : name;
    }

    // =========================================================
    // DISPOSE
    // =========================================================

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
            await _browser.DisposeAsync();

        _playwright?.Dispose();
    }
}

