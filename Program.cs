using Discord;
using Discord.WebSocket;

using DotNetEnv;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Services;

// =========================================================
// LOAD ENV
// =========================================================

Env.Load();

// =========================================================
// HOST BUILDER
// =========================================================

var builder = Host.CreateApplicationBuilder(args);

// =========================================================
// CONFIGURATION
// =========================================================

builder.Services.AddOptions<BotConfiguration>()
    .Bind(builder.Configuration.GetSection(BotConfiguration.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// =========================================================
// LOGGING
// =========================================================

builder.Logging.ClearProviders();

builder.Logging.AddConsole();

// =========================================================
// DISCORD SOCKET CLIENT
// =========================================================

builder.Services.AddSingleton<DiscordSocketClient>(_ =>
{
    var config = new DiscordSocketConfig
    {
        GatewayIntents =
            GatewayIntents.Guilds |
            GatewayIntents.GuildMessages,

        LogGatewayIntentWarnings = false
    };

    return new DiscordSocketClient(config);
});

// =========================================================
// CORE SERVICES
// =========================================================

builder.Services.AddSingleton<DiscordService>();

builder.Services.AddSingleton<PersistenceService>();

builder.Services.AddSingleton<LiveStateService>();

builder.Services.AddSingleton<ShopService>();

builder.Services.AddSingleton<ShopMessagePersistenceService>();

// =========================================================
// HTTP CLIENTS
// =========================================================

builder.Services.AddHttpClient<YouTubeApiService>();

builder.Services.AddHttpClient<LdShopScraperService>();

// =========================================================
// BACKGROUND SERVICES
// =========================================================

builder.Services.AddHostedService<YouTubeCheckerBackgroundService>();

builder.Services.AddHostedService<ShopBackgroundService>();

// =========================================================
// BUILD APP
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