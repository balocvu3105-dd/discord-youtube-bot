# Discord YouTube & Shop Bot

[![Build](https://github.com/balocvu3105-dd/discord-youtube-bot/actions/workflows/dotnet.yml/badge.svg)](https://github.com/balocvu3105-dd/discord-youtube-bot/actions/workflows/dotnet.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker)](https://www.docker.com/)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

A production-grade Discord bot built with **.NET 8** and **C#**, deployed on a Linux VPS via Docker. It handles YouTube channel monitoring and an extensible multi-provider game shop discount board with affiliate link tracking and scheduled embed refresh.

The shop system is built around a **provider pattern** — currently integrating **LDShop** and **Lootbar**, with the architecture ready to add new shops (Codashop, Midasbuy, Garena, etc.) by implementing a single interface.

> **Live in production** — serving an active Vietnamese Discord community for a game top-up affiliate shop.

---

## Features

### YouTube Monitoring
- Polls YouTube Data API v3 on a configurable interval to detect new video uploads and livestream start/end events
- Sends rich Discord embeds to dedicated channels with role pings and full deduplication
- Restart-safe: survives container restarts without re-sending notifications via persistent JSON state

### Multi-Provider Game Shop Discount Board
- Aggregates real-time discount data from multiple independent shop providers (**LDShop** and **Lootbar**)
- Each provider renders its own Discord embed with live discount percentages and affiliate buttons
- Extensible by design: adding a new shop requires only implementing `IShopDiscountProvider` — no changes to existing code
- Scheduled auto-refresh at **00:00** and **12:00** (Vietnam time, UTC+7) via a `BackgroundService`
- `/refreshshop` slash command (Admin only) for on-demand refresh — edits existing messages in-place, never creates duplicates
- Persists Discord message IDs so embeds survive bot restarts

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     .NET 8 Generic Host                      │
│                                                              │
│  ┌───────────────────────┐   ┌──────────────────────────┐   │
│  │  YouTubeChecker       │   │  ShopBackgroundService   │   │
│  │  BackgroundService    │   │  (00:00 & 12:00 VN time) │   │
│  └──────────┬────────────┘   └────────────┬─────────────┘   │
│             │                             │                  │
│             ▼                             ▼                  │
│  ┌──────────────────────┐  ┌─────────────────────────────┐  │
│  │  YouTubeApiService   │  │    ShopDiscountAggregator   │  │
│  │  (YouTube Data API)  │  │  ┌───────────────────────┐  │  │
│  └──────────────────────┘  │  │  LdShopDiscountProvider│  │  │
│                             │  ├───────────────────────┤  │  │
│                             │  │ LootbarDiscountProvider│  │  │
│                             │  └───────────────────────┘  │  │
│                             └─────────────────────────────┘  │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Discord Interaction Modules              │   │
│  │           /refreshshop  (Administrator only)         │   │
│  └──────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 8, C# |
| Discord | Discord.Net (Interactions API) |
| External APIs | YouTube Data API v3, LDShop API, Lootbar API |
| HTTP | `IHttpClientFactory` + Polly standard resilience pipeline |
| Logging | Serilog (structured console + daily rolling file, 7-day retention) |
| Configuration | `IOptions<T>` with DataAnnotations validation on startup |
| Background jobs | `IHostedService` / `BackgroundService` |
| Containerization | Docker, docker-compose |
| CI/CD | GitHub Actions |
| Deployment | Ubuntu 22.04 VPS |

---

## Design Patterns

**Provider / Strategy** — `IShopDiscountProvider` defines a uniform interface; `LdShopDiscountProvider` and `LootbarDiscountProvider` are two current implementations composed by `ShopDiscountAggregator`. Adding a new shop requires only a new class registered with DI — zero changes to existing code (Open/Closed Principle). The aggregator runs all providers in parallel via `Task.WhenAll`.

**IHttpClientFactory** — each external API gets its own named `HttpClient` with provider-specific headers, GZip/Deflate decompression, and Polly standard resilience (retry with exponential backoff + circuit breaker).

**Options pattern** — all configuration is bound from `appsettings.json` via `IOptions<BotConfiguration>`, with `ValidateDataAnnotations()` and `ValidateOnStart()` to catch misconfiguration at boot rather than at runtime.

**Upsert messaging** — on each refresh cycle the bot loads persisted Discord message IDs. If the message exists it calls `ModifyAsync`; if deleted it creates a new one and saves the new ID. This guarantees exactly one embed per provider at all times without spam.

**Singleton + IHostedService dual registration** — `ShopBackgroundService` is registered as `AddSingleton<T>()` and then as `AddHostedService(sp => sp.GetRequiredService<T>())`. This lets `ShopCommandModule` inject and call `RefreshShopAsync()` directly while the scheduler continues running as a hosted background task.

---

## Project Structure

```
Background/
  ShopBackgroundService.cs           # Scheduled shop refresh (timezone-aware, 00:00 & 12:00 VN)
  YouTubeCheckerBackgroundService.cs # Polls YouTube API for new videos and live events

Commands/
  ShopCommandModule.cs               # /refreshshop slash command

Config/
  BotConfiguration.cs                # Strongly-typed configuration model

Models/
  Models.cs                          # ShopGameConfig, ShopMessageState, etc.

Services/
  IServices.cs                       # Service & provider interfaces
  IShopDiscountProvider.cs           # Shop provider contract
  ShopService.cs                     # Builds Discord embeds for LDShop & Lootbar
  ShopDiscountAggregator.cs          # Composes IShopDiscountProvider list, runs in parallel
  LdShopDiscountProvider.cs
  LdShopDiscountService.cs           # LDShop HTTP API client
  LootbarDiscountProvider.cs
  LootbarDiscountService.cs          # Lootbar HTTP API client (app_service_id mapping)
  LootbarScraperService.cs           # Lootbar HTML scraper fallback
  ShopMessagePersistenceService.cs   # Persists Discord message IDs to JSON
  DiscordService.cs                  # Discord client connection & event wiring
  YouTubeApiService.cs               # YouTube Data API v3 wrapper
  LiveStateService.cs                # Tracks current live stream state
  PersistenceService.cs              # Last-video-state persistence
  AsyncJsonStore.cs                  # Thread-safe generic JSON persistence base class

Program.cs                           # DI composition root
Dockerfile
appsettings.json
```

---

## Setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Docker & docker-compose
- A Discord Bot Token — create one at the [Discord Developer Portal](https://discord.com/developers/applications)
  - Required scopes: `bot`, `applications.commands`
  - Required bot permissions: `Send Messages`, `Embed Links`, `Mention Everyone` (for role pings)
  - Required gateway intents: `GUILDS`, `GUILD_MESSAGES`
- A [YouTube Data API v3](https://console.cloud.google.com/) key

### Environment Variables

Create `.env` next to `docker-compose.yml` (or `appsettings.json` for local dev). **Never commit secrets to git.**

```env
BotConfiguration__DiscordToken=YOUR_DISCORD_BOT_TOKEN
BotConfiguration__YoutubeApiKey=YOUR_YOUTUBE_API_KEY
```

### Configuration

All other settings live in `appsettings.json`:

```json
{
  "BotConfiguration": {
    "DiscordToken": "",
    "YoutubeApiKey": "",
    "YoutubeChannelId": "UCxxxxxxxxxxxxxxx",

    "LiveChannelId":  1234567890123456789,
    "VideoChannelId": 1234567890123456789,
    "ShopChannelId":  1234567890123456789,

    "LiveRoleId":  1234567890123456789,
    "VideoRoleId": 1234567890123456789,

    "CheckIntervalSeconds": 120,

    "StateFilePath":     "data/last_video_state.json",
    "LiveStateFilePath": "data/live_state.json",

    "ShopNotice": "Optional announcement text shown above the shop embeds",

    "LootbarShopCode": "YourShopCode",
    "LootbarShopLink": "https://www.lootbar.com/shop/YourShopCode",

    "ShopGames": [
      {
        "Name":         "Wuthering Waves",
        "Emoji":        "🌊",
        "CommodityId":  10016,
        "SkuLabelId":   74,
        "AffiliateLink": "https://chain.ldshop.gg/...",
        "DiscountPercent": 10,
        "PromoNote":    "Tiết kiệm ngay khi nạp Lunite!",
        "TopUpType":    "LDShop đăng nhập hộ",
        "Warning":      "⚠️ Không đăng nhập game trong lúc đang xử lý",
        "LootbarGameSeo":       "wuthering-waves",
        "LootbarAffiliateLink": "https://www.lootbar.com/top-up/wuthering-waves?shop_code=YourCode",
        "LootbarFallbackDiscount": 0,
        "LootbarAppServiceId":  89
      }
    ]
  }
}
```

| Field | Description |
|---|---|
| `YoutubeChannelId` | The YouTube channel ID to monitor (`UC...`) |
| `LiveChannelId` / `VideoChannelId` | Discord channel IDs for live/video notifications |
| `ShopChannelId` | Discord channel ID where shop embeds are posted |
| `LiveRoleId` / `VideoRoleId` | Role IDs to ping on new livestream / video |
| `CheckIntervalSeconds` | How often to poll YouTube API (default: 120s) |
| `ShopNotice` | Optional text pinned above shop embeds (e.g. promotions) |
| `LootbarShopCode` | Your Lootbar shop referral code |
| `CommodityId` / `SkuLabelId` | LDShop internal IDs for the game's discount lookup |
| `LootbarAppServiceId` | Lootbar's `app_service_id` for the game |
| `LootbarFallbackDiscount` | Fallback discount % if Lootbar API returns no data |

### Docker Compose (Production)

Create `docker-compose.yml` next to `Dockerfile`:

```yaml
services:
  bot:
    build: .
    restart: unless-stopped
    env_file: .env
    volumes:
      - ./data:/app/data
      - ./logs:/app/logs
```

Then deploy:

```bash
docker compose up --build -d
docker compose logs -f
```

### Run Locally

```bash
dotnet restore
dotnet run
```

---

## Slash Commands

| Command | Permission | Description |
|---|---|---|
| `/refreshshop` | Administrator | Force-refresh all shop embeds in-place (no duplicate messages) |

---

## CI/CD

GitHub Actions runs on every push to `main`: restore → build Release → validate. Docker image is built on the VPS from source via `docker compose up --build`.

---

## Adding a New Shop Provider

1. Create `Services/NewShopDiscountProvider.cs` implementing `IShopDiscountProvider`
2. Register in `Program.cs`:
   ```csharp
   builder.Services.AddSingleton<IShopDiscountProvider, NewShopDiscountProvider>();
   ```
3. Done — `ShopDiscountAggregator` picks it up automatically. No other changes needed.

---

## Notable Implementation Details

- **Timezone-aware scheduling**: next refresh is calculated in Vietnam Standard Time (UTC+7) using `TimeZoneInfo`, correctly handling the midnight boundary.
- **Lazy IEnumerable guard**: provider list is materialized with `.ToList()` before `Task.WhenAll` — without this, LINQ deferred execution silently skipped providers on the first run.
- **Async ref workaround**: `UpsertMessageAsync` returns `(bool changed, ulong messageId)` instead of `ref ulong` — async methods cannot have `ref` parameters in C#.
- **Resilience**: all external HTTP calls go through Polly's `AddStandardResilienceHandler` — automatic retries with exponential backoff and circuit breaking on transient failures.
- **Structured logging**: Serilog writes JSON-structured logs to both console and daily rolling files (7-day retention), with Discord and Microsoft noise suppressed to `Warning` level.

---

## License

MIT
