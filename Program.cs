using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. CONFIGURATION
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// 2. LOGGING
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// 3. DEPENDENCY INJECTION (DI)
builder.Services.Configure<BotConfiguration>(
    builder.Configuration.GetSection(BotConfiguration.SectionName));

// Đăng ký các Service xử lý logic
builder.Services.AddSingleton<DiscordService>();
builder.Services.AddSingleton<YouTubeApiService>(); // ĐÃ ĐỔI TÊN Ở ĐÂY
builder.Services.AddSingleton<PersistenceService>();

// Đăng ký Worker chạy ngầm
builder.Services.AddHostedService<YouTubeCheckerBackgroundService>();
builder.Services.AddSingleton<LiveStateService>();

var app = builder.Build();

// 4. HEALTH CHECK (Cho Render & UptimeRobot)
app.MapMethods("/", new[] { "GET", "HEAD" }, () => Results.Ok("Bot is running..."));

// 5. START DISCORD CONNECTION
var discordService = app.Services.GetRequiredService<DiscordService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("🚀 Discord Bot is connecting...");
    await discordService.ConnectAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "❌ Bot crashed during startup!");
}

await app.RunAsync();