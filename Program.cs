using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.aConfig;
using YouTubeDiscordBot.Services;

var builder = WebApplication.CreateBuilder(args);

// CONFIG
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// LOG
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// SERVICES
builder.Services.Configure<BotConfiguration>(
    builder.Configuration.GetSection(BotConfiguration.SectionName));

builder.Services.AddSingleton<DiscordService>();
builder.Services.AddSingleton<YouTubeService>();
builder.Services.AddSingleton<PersistenceService>();
builder.Services.AddHostedService<YouTubeCheckerBackgroundService>();

var app = builder.Build();

// ✅ Health check cho Render + UptimeRobot
app.MapMethods("/", new[] { "GET", "HEAD" }, () => Results.Ok("OK"));

// Start bot khi app start
var discordService = app.Services.GetRequiredService<DiscordService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Bot starting...");
    await discordService.ConnectAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Bot crash khi start!");
}

await app.RunAsync();