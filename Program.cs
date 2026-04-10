using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Config;
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

// ✅ Web endpoint cho Render biết app còn sống
app.MapGet("/", () => "YouTube Discord Bot is running");

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

// Run web + background services
await app.RunAsync();