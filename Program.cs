using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

// Load .env
Env.Load();

var builder = Host.CreateApplicationBuilder(args);

// =========================================================
// CONFIG
// =========================================================

builder.Services.Configure<BotConfiguration>(
    builder.Configuration.GetSection("BotConfiguration"));

// =========================================================
// LOGGING
// =========================================================

builder.Logging.ClearProviders();

builder.Logging.AddConsole();

// =========================================================
// SERVICES
// =========================================================

builder.Services.AddSingleton<DiscordService>();

// ✅ FIX
builder.Services.AddHttpClient<YouTubeApiService>();

builder.Services.AddSingleton<PersistenceService>();

builder.Services.AddSingleton<LiveStateService>();

builder.Services.AddSingleton<PromoService>();

builder.Services.AddSingleton<ShopInfoService>();

// =========================================================
// BACKGROUND SERVICES
// =========================================================

builder.Services.AddHostedService<
    YouTubeCheckerBackgroundService>();

builder.Services.AddHostedService<
    PromoBackgroundService>();

builder.Services.AddHostedService<
    ShopInfoBackgroundService>();

// =========================================================
// BUILD
// =========================================================

var app = builder.Build();

// =========================================================
// CONNECT DISCORD
// =========================================================

var discord =
    app.Services.GetRequiredService<DiscordService>();

await discord.ConnectAsync();

// =========================================================
// RUN
// =========================================================

await app.RunAsync();