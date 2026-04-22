using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

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
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.ConfigureServices((context, services) =>
{
    // Config
    services.Configure<BotConfiguration>(
        context.Configuration.GetSection(BotConfiguration.SectionName));

    // Services
    services.AddSingleton<DiscordService>();
    services.AddSingleton<YouTubeApiService>();
    services.AddSingleton<PersistenceService>();
    services.AddSingleton<LiveStateService>();

    // Worker
    services.AddHostedService<YouTubeCheckerBackgroundService>();
});

var host = builder.Build();

// 🔥 connect Discord trước khi chạy loop
var discord = host.Services.GetRequiredService<DiscordService>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("🚀 Discord Bot is connecting...");
    await discord.ConnectAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "❌ Bot crashed during startup!");
}

await host.RunAsync();