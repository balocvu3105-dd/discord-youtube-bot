using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

var host = Host.CreateDefaultBuilder(args)

    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();
    })

    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Debug);
    })

    .ConfigureServices((context, services) =>
    {
        services.Configure<BotConfiguration>(
            context.Configuration.GetSection(BotConfiguration.SectionName));

        services.AddSingleton<DiscordService>();
        services.AddSingleton<YouTubeService>();
        services.AddSingleton<PersistenceService>();

        services.AddHostedService<YouTubeCheckerBackgroundService>();
    })
    .Build();

var discordService = host.Services.GetRequiredService<DiscordService>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("========================================");
    logger.LogInformation("  YouTube Discord Notification Bot");
    logger.LogInformation("========================================");

    await discordService.ConnectAsync();

    logger.LogInformation("Đang khởi động các background service...");

    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Bot bị crash khi khởi động!");
}
finally
{
    await discordService.DisposeAsync();
    logger.LogInformation("Bot đã tắt. Tạm biệt!");
}