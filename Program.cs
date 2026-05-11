using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;
using YouTubeDiscordBot.Commands;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureAppConfiguration((context, config) =>
{
    config
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();
});

builder.ConfigureLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddFilter("Microsoft", LogLevel.Warning);
});

builder.ConfigureServices((context, services) =>
{
    // Config
    services.Configure<BotConfiguration>(
        context.Configuration.GetSection(BotConfiguration.SectionName));

    // Core services
    services.AddSingleton<DiscordService>();
    services.AddSingleton<YouTubeApiService>();
    services.AddSingleton<PersistenceService>();
    services.AddSingleton<LiveStateService>();

    // Promo
    services.AddSingleton<PromoService>();
    services.AddHostedService<PromoBackgroundService>();

    // ↓ THÊM 3 DÒNG NÀY VÀO ĐÂY ↓
    services.AddSingleton<LdShopScraperService>();
    services.AddSingleton<PromoChangeDetectorService>();
    services.AddHostedService<PromoChangeBackgroundService>();

    // Thêm vào ngay sau dòng "// Promo"
    services.AddSingleton<ShopInfoService>();
    services.AddHostedService<ShopInfoBackgroundService>();

    // Slash commands — InteractionService của Discord.Net
    services.AddSingleton(provider =>
    {
        var discord = provider.GetRequiredService<DiscordService>();
        return new InteractionService(discord.Client);
    });

    // YouTube checker
    services.AddHostedService<YouTubeCheckerBackgroundService>();
});

var host = builder.Build();

var discord = host.Services.GetRequiredService<DiscordService>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var interactionService = host.Services.GetRequiredService<InteractionService>();

try
{
    logger.LogInformation("🚀 Discord Bot is connecting...");
    await discord.ConnectAsync();

    // Đăng ký tất cả slash command từ assembly hiện tại
    await interactionService.AddModulesAsync(
        assembly: System.Reflection.Assembly.GetEntryAssembly(),
        services: host.Services);

    // Đăng ký lên Discord khi bot Ready
    discord.Client.Ready += async () =>
    {
        // RegisterCommandsGloballyAsync: đăng ký toàn cầu (mất ~1h để cập nhật)
        // RegisterCommandsToGuildAsync(guildId): đăng ký 1 server (tức thì — dùng khi test)
        await interactionService.RegisterCommandsGloballyAsync();
        logger.LogInformation("✅ Slash commands registered globally");
    };

    // Xử lý khi user dùng slash command
    discord.Client.InteractionCreated += async interaction =>
    {
        var ctx = new SocketInteractionContext(discord.Client, interaction);
        await interactionService.ExecuteCommandAsync(ctx, host.Services);
    };
}
catch (Exception ex)
{
    logger.LogCritical(ex, "❌ Bot crashed during startup!");
}

await host.RunAsync();