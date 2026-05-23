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
    builder.Services.AddHttpClient<LdShopScraperService>()
        .AddStandardResilienceHandler();

    // FIX: thêm LdShopDiscountService để ShopService inject được
    builder.Services.AddHttpClient<LdShopDiscountService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                 System.Net.DecompressionMethods.Deflate
    })
    .AddStandardResilienceHandler();
    builder.Services.AddTransient<LdShopDiscountService>();

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