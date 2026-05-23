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

// ═══════════════════════════════════════════════════════════════════
// SERILOG SETUP
// ═══════════════════════════════════════════════════════════════════
// Setup trước host builder để catch lỗi startup.

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Discord.Net rất verbose — giảm xuống Warning để log sạch hơn
    .MinimumLevel.Override("Discord", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    // Console: có màu, có timestamp
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext:l} — {Message:lj}{NewLine}{Exception}")
    // File: rotate hằng ngày, giữ 7 ngày — hữu ích khi debug trên VPS
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

    // Load .env — chứa DISCORD_TOKEN, YOUTUBE_API_KEY, v.v.
    Env.Load();

    var builder = Host.CreateApplicationBuilder(args);

    // Dùng Serilog thay vì Microsoft.Extensions.Logging mặc định
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    // ── Config ──────────────────────────────────────────────────────────
    // ValidateDataAnnotations + ValidateOnStart: fail fast nếu config thiếu/sai
    builder.Services.AddOptions<BotConfiguration>()
        .Bind(builder.Configuration.GetSection(BotConfiguration.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ── Discord Socket Client ────────────────────────────────────────────
    builder.Services.AddSingleton<DiscordSocketClient>(_ =>
        new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds |
                GatewayIntents.GuildMessages,
            LogGatewayIntentWarnings = false
        }));

    // ── Interaction Service (Slash Commands) ─────────────────────────────
    builder.Services.AddSingleton(sp =>
    {
        var client = sp.GetRequiredService<DiscordSocketClient>();
        return new InteractionService(client, new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Warning,
            DefaultRunMode = RunMode.Async
        });
    });

    // ── Services ─────────────────────────────────────────────────────────
    // DiscordService đăng ký cả concrete lẫn interface:
    //   - Concrete: Background services cần WaitForReadyAsync()
    //   - Interface: các service khác inject IDiscordService để dễ test
    builder.Services.AddSingleton<DiscordService>();
    builder.Services.AddSingleton<IDiscordService>(sp =>
        sp.GetRequiredService<DiscordService>());

    builder.Services.AddSingleton<IPersistenceService, PersistenceService>();
    builder.Services.AddSingleton<ILiveStateService, LiveStateService>();
    builder.Services.AddSingleton<IShopService, ShopService>();
    builder.Services.AddSingleton<IShopMessagePersistenceService, ShopMessagePersistenceService>();

    // ── HTTP Clients với Retry Policy ────────────────────────────────────
    // AddStandardResilienceHandler() là Polly 8 native pattern cho .NET 8.
    // Tự động thêm: retry (3 lần, exponential backoff), timeout, circuit breaker.
    builder.Services.AddHttpClient<LdShopScraperService>()
        .AddStandardResilienceHandler();

    // LdShopDiscountService: fetch discount tự động cho shop embeds
    builder.Services.AddHttpClient<LdShopDiscountService>()
        .AddStandardResilienceHandler();

    // YouTubeApiService dùng Google SDK (tự quản lý HttpClient) → AddSingleton trực tiếp
    builder.Services.AddSingleton<IYouTubeApiService, YouTubeApiService>();

    // ── Background Services ──────────────────────────────────────────────
    builder.Services.AddHostedService<YouTubeCheckerBackgroundService>();
    builder.Services.AddHostedService<ShopBackgroundService>();

    // ── Command Modules (Transient) ──────────────────────────────────────
    builder.Services.AddTransient<ShopCommandModule>();

    // ═══════════════════════════════════════════════════════════════════
    // BUILD
    // ═══════════════════════════════════════════════════════════════════

    var app = builder.Build();

    // ── Setup Slash Commands ─────────────────────────────────────────────
    var interactionService = app.Services.GetRequiredService<InteractionService>();
    var discordClient = app.Services.GetRequiredService<DiscordSocketClient>();

    // Load tất cả InteractionModuleBase trong assembly
    await interactionService.AddModulesAsync(
        assembly: System.Reflection.Assembly.GetExecutingAssembly(),
        services: app.Services);

    // Đăng ký commands khi Discord ready
    discordClient.Ready += async () =>
    {
        // RegisterCommandsGloballyAsync: sync đến tất cả servers (mất ~1 giờ lần đầu)
        // Nếu muốn instant (chỉ 1 guild): RegisterCommandsToGuildAsync(guildId)
        await interactionService.RegisterCommandsGloballyAsync();
        Log.Information("Slash commands registered globally");
    };

    // Route interaction events đến đúng handler
    discordClient.InteractionCreated += async interaction =>
    {
        var ctx = new SocketInteractionContext(discordClient, interaction);
        await interactionService.ExecuteCommandAsync(ctx, app.Services);
    };

    // ── Connect Discord ──────────────────────────────────────────────────
    var discordService = app.Services.GetRequiredService<DiscordService>();
    await discordService.ConnectAsync();

    // ── Run ──────────────────────────────────────────────────────────────
    await app.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    // Flush tất cả log buffer trước khi process exit
    await Log.CloseAndFlushAsync();
}