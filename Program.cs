using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using DotNetEnv;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;

using YouTubeDiscordBot.Background;
using YouTubeDiscordBot.Commands;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Discord", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext:l} — {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/bot-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext:l} — {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("═══════════════════════════════════════");
    Log.Information("YouTubeDiscordBot starting...");
    Log.Information("═══════════════════════════════════════");

    Env.Load();

    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.AddOptions<BotConfiguration>()
        .Bind(builder.Configuration.GetSection(BotConfiguration.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddSingleton<DiscordSocketClient>(_ =>
        new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds |
                GatewayIntents.GuildMessages,
            LogGatewayIntentWarnings = false
        }));

    builder.Services.AddSingleton(sp =>
    {
        var client = sp.GetRequiredService<DiscordSocketClient>();
        return new InteractionService(client, new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Warning,
            DefaultRunMode = RunMode.Async
        });
    });

    builder.Services.AddSingleton<DiscordService>();
    builder.Services.AddSingleton<IDiscordService>(sp =>
        sp.GetRequiredService<DiscordService>());

    builder.Services.AddSingleton<IPersistenceService, PersistenceService>();
    builder.Services.AddSingleton<ILiveStateService, LiveStateService>();
    builder.Services.AddSingleton<IShopService, ShopService>();
    builder.Services.AddSingleton<IShopMessagePersistenceService, ShopMessagePersistenceService>();

    // ── HTTP Clients ─────────────────────────────────────────────────────

    // LDShop
    builder.Services.AddHttpClient(nameof(LdShopDiscountService))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate
        })
        .ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Origin", "https://www.ldshop.gg");
            client.DefaultRequestHeaders.Add("Referer", "https://www.ldshop.gg/");
            client.DefaultRequestHeaders.Add("Channel", "ldshop");
            client.DefaultRequestHeaders.Add("Currency", "VND");
            client.DefaultRequestHeaders.Add("Cversion", "v2");
            client.DefaultRequestHeaders.Add("Language", "vn");
            client.DefaultRequestHeaders.Add("Source", "pc");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-site");
        })
        .AddStandardResilienceHandler();

    builder.Services.AddSingleton<LdShopDiscountService>();

    // Lootbar
    builder.Services.AddHttpClient(nameof(LootbarDiscountService))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate
        })
        .ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Origin", "https://www.lootbar.com");
            client.DefaultRequestHeaders.Add("Referer", "https://www.lootbar.com/");
            client.DefaultRequestHeaders.Add("x-currency", "VND");
            client.DefaultRequestHeaders.Add("x-ps-app-version-code", "v20260615");
            client.DefaultRequestHeaders.Add("x-ps-locale", "en");
            client.DefaultRequestHeaders.Add("x-ps-os-type", "Android");
            client.DefaultRequestHeaders.Add("x-ps-system-type", "mobile_web");
            client.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
            client.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
            client.DefaultRequestHeaders.Add("sec-fetch-site", "same-site");
        })
        .AddStandardResilienceHandler();

    builder.Services.AddSingleton<LootbarDiscountService>();

    // ── Shop Providers ───────────────────────────────────────────────────
    // Thứ tự đăng ký = thứ tự hiển thị khi cùng discount.
    // Để add shop mới: AddSingleton<IShopDiscountProvider, NewProvider>()
    builder.Services.AddSingleton<IShopDiscountProvider, LdShopDiscountProvider>();
    builder.Services.AddSingleton<IShopDiscountProvider, LootbarDiscountProvider>();

    builder.Services.AddSingleton<ShopDiscountAggregator>();

    builder.Services.AddSingleton<IYouTubeApiService, YouTubeApiService>();

    // ── TikTok ───────────────────────────────────────────────────────────
    // TikTokService dùng Python subprocess (tiktok_check.py) — không cần HttpClient
    builder.Services.AddSingleton<ITikTokService, TikTokService>();

    // ── Multi-Platform Streamer Tracking (Twitch, Kick, FB...) ───────────
    builder.Services.AddSingleton<StreamerManagerService>();

    builder.Services.AddHttpClient(nameof(TwitchService))
        .ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        })
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient(nameof(KickService))
        .ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Origin", "https://kick.com");
            client.DefaultRequestHeaders.Add("Referer", "https://kick.com/");
        })
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient(nameof(FacebookLiveService))
        .ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,vi;q=0.8");
            client.DefaultRequestHeaders.Add("sec-fetch-dest", "document");
            client.DefaultRequestHeaders.Add("sec-fetch-mode", "navigate");
            client.DefaultRequestHeaders.Add("sec-fetch-site", "none");
        })
        .AddStandardResilienceHandler();

    builder.Services.AddSingleton<IStreamPlatformProvider, TwitchService>();
    builder.Services.AddSingleton<IStreamPlatformProvider, KickService>();
    builder.Services.AddSingleton<IStreamPlatformProvider, FacebookLiveService>();

    // ── Background Services ──────────────────────────────────────────────
    builder.Services.AddHostedService<YouTubeCheckerBackgroundService>();
    builder.Services.AddHostedService<TikTokCheckerBackgroundService>();
    builder.Services.AddHostedService<UnifiedStreamCheckerBackgroundService>();

    // AddSingleton trước để có thể inject ShopBackgroundService vào ShopCommandModule
    builder.Services.AddSingleton<ShopBackgroundService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ShopBackgroundService>());

    // ── Command Modules ──────────────────────────────────────────────────
    builder.Services.AddTransient<ShopCommandModule>();
    builder.Services.AddTransient<LiveManagementCommandModule>();

    var app = builder.Build();

    var interactionService = app.Services.GetRequiredService<InteractionService>();
    var discordClient = app.Services.GetRequiredService<DiscordSocketClient>();

    await interactionService.AddModulesAsync(
        assembly: System.Reflection.Assembly.GetExecutingAssembly(),
        services: app.Services);

    discordClient.Ready += async () =>
    {
        try
        {
            await interactionService.RegisterCommandsGloballyAsync();
            Log.Information("Slash commands registered globally");
        }
        catch (Exception ex)
        {
            // Exception trong async event handler bị swallowed bởi Discord.Net.
            // Log thủ công để không mất trace khi lệnh slash không đăng ký được.
            Log.Error(ex, "RegisterCommandsGloballyAsync thất bại");
        }
    };

    discordClient.InteractionCreated += async interaction =>
    {
        try
        {
            var ctx = new SocketInteractionContext(discordClient, interaction);
            await interactionService.ExecuteCommandAsync(ctx, app.Services);
        }
        catch (Exception ex)
        {
            // Exception trong async event handler bị swallowed — log để không mất trace.
            Log.Error(ex, "InteractionCreated handler thất bại");
        }
    };

    var discordService = app.Services.GetRequiredService<DiscordService>();
    await discordService.ConnectAsync();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}