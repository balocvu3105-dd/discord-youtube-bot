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

    // FIX: Chỉ đăng ký 1 lần — DiscordService implement IDiscordService.
    // Background services dùng IDiscordService (có WaitForReadyAsync) — không cần inject concrete nữa.
    builder.Services.AddSingleton<DiscordService>();
    builder.Services.AddSingleton<IDiscordService>(sp =>
        sp.GetRequiredService<DiscordService>());

    builder.Services.AddSingleton<IPersistenceService, PersistenceService>();
    builder.Services.AddSingleton<ILiveStateService, LiveStateService>();
    builder.Services.AddSingleton<IShopService, ShopService>();
    builder.Services.AddSingleton<IShopMessagePersistenceService, ShopMessagePersistenceService>();

    // ── HTTP Clients ─────────────────────────────────────────────────────
    // LdShopScraperService — giữ đăng ký để không break nếu dùng lại sau
    builder.Services.AddHttpClient<LdShopScraperService>()
        .AddStandardResilienceHandler();

    // FIX: LdShopDiscountService phải là Singleton vì có in-memory cache (_cache).
    // Transient = tạo mới mỗi lần inject → cache bị reset → WarmCacheAsync vô tác dụng.
    //
    // AddHttpClient<T> mặc định đăng ký T là Transient (vì HttpClient không thread-safe để share).
    // Để có Singleton với HttpClient, dùng IHttpClientFactory inject vào constructor.
    // LdShopDiscountService nhận HttpClient qua constructor → AddHttpClient vẫn dùng được,
    // nhưng phải override registration về Singleton sau khi AddHttpClient.
    builder.Services.AddHttpClient<LdShopDiscountService>()
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate
        })
        .AddStandardResilienceHandler();
    // Override về Singleton — AddHttpClient đã đăng ký Transient, ta replace bằng Singleton
    builder.Services.AddSingleton<LdShopDiscountService>(sp =>
        ActivatorUtilities.CreateInstance<LdShopDiscountService>(sp));

    builder.Services.AddSingleton<IYouTubeApiService, YouTubeApiService>();

    // ── Background Services ──────────────────────────────────────────────
    builder.Services.AddHostedService<YouTubeCheckerBackgroundService>();
    builder.Services.AddHostedService<ShopBackgroundService>();

    // ── Command Modules ──────────────────────────────────────────────────
    builder.Services.AddTransient<ShopCommandModule>();

    var app = builder.Build();

    var interactionService = app.Services.GetRequiredService<InteractionService>();
    var discordClient = app.Services.GetRequiredService<DiscordSocketClient>();

    await interactionService.AddModulesAsync(
        assembly: System.Reflection.Assembly.GetExecutingAssembly(),
        services: app.Services);

    discordClient.Ready += async () =>
    {
        await interactionService.RegisterCommandsGloballyAsync();
        Log.Information("Slash commands registered globally");
    };

    discordClient.InteractionCreated += async interaction =>
    {
        var ctx = new SocketInteractionContext(discordClient, interaction);
        await interactionService.ExecuteCommandAsync(ctx, app.Services);
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