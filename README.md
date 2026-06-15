# Discord YouTube & Shop Bot

[![Build](https://github.com/balocvu3105-dd/discord-youtube-bot/actions/workflows/dotnet.yml/badge.svg)](https://github.com/balocvu3105-dd/discord-youtube-bot/actions/workflows/dotnet.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker)](https://www.docker.com/)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

A production-grade Discord bot built with **.NET 8** and **C#**, deployed on a Linux VPS via Docker. It handles YouTube channel monitoring and a real-time game shop discount board with multi-provider aggregation, affiliate link tracking, and scheduled embed refresh.

> **Live in production** — serving an active Discord community for a game top-up affiliate shop.

---

## Features

### YouTube Monitoring
- Polls YouTube Data API v3 on a configurable interval to detect new video uploads and livestream events
- Sends rich Discord embeds to dedicated channels with full deduplication
- Restart-safe: survives container restarts without re-sending notifications via persistent JSON state

### Game Shop Discount Board
- Aggregates real-time discount data from **two independent providers**: LDShop and Lootbar
- Renders two separate Discord embeds (one per provider) with live discount percentages and affiliate buttons
- Scheduled auto-refresh at 00:00 and 12:00 (Vietnam time, UTC+7) via a `BackgroundService`
- `/refreshshop` slash command (Admin only) for on-demand refresh — edits existing messages in-place, never creates duplicates
- Persists Discord message IDs to `ShopMessageState` so embeds survive bot restarts

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
| Logging | Serilog (structured console + daily rolling file) |
| Configuration | `IOptions<T>` with DataAnnotations validation on startup |
| Background jobs | `IHostedService` / `BackgroundService` |
| Containerization | Docker, docker-compose |
| CI/CD | GitHub Actions |
| Deployment | Ubuntu 22.04 VPS |

---

## Design Patterns

**Provider / Strategy** — `IShopDiscountProvider` defines a uniform interface; `LdShopDiscountProvider` and `LootbarDiscountProvider` are independent implementations composed by `ShopDiscountAggregator`. Adding a new shop requires only a new class — zero changes to existing code (Open/Closed Principle).

**IHttpClientFactory** — each external API gets its own named `HttpClient` with provider-specific headers, compression (`GZip`/`Deflate`), and Polly standard resilience (retry with exponential backoff + circuit breaker).

**Options pattern** — all configuration is bound from `appsettings.json` via `IOptions<BotConfiguration>`, with `ValidateDataAnnotations()` and `ValidateOnStart()` to catch misconfiguration at boot rather than at runtime.

**Upsert messaging** — on each refresh cycle the bot loads persisted Discord message IDs. If the message exists it calls `ModifyAsync`; if deleted it creates a new one and saves the new ID. This guarantees exactly one embed per provider at all times without spam.

**Singleton + IHostedService dual registration** — `ShopBackgroundService` is registered as `AddSingleton<T>()` and then as `AddHostedService(sp => sp.GetRequiredService<T>())`. This lets `ShopCommandModule` inject and call `RefreshShopAsync()` directly while the scheduler continues running as a hosted background task.

---

## Project Structure

```
Background/
  ShopBackgroundService.cs          # Scheduled shop refresh (timezone-aware)
  YouTubeCheckerBackgroundService.cs

Commands/
  ShopCommandModule.cs              # /refreshshop slash command

Config/
  BotConfiguration.cs               # Strongly-typed configuration model

Models/
  Models.cs                         # ShopGameConfig, ShopMessageState, etc.

Services/
  IServices.cs                      # Service & provider interfaces
  ShopService.cs                    # Builds Discord embeds for LDShop & Lootbar
  ShopDiscountAggregator.cs         # Composes IShopDiscountProvider list
  LdShopDiscountProvider.cs
  LdShopDiscountService.cs          # LDShop HTTP API client
  LootbarDiscountProvider.cs
  LootbarDiscountService.cs         # Lootbar HTTP API client (app_service_id mapping)
  ShopMessagePersistenceService.cs  # Persists Discord message IDs
  DiscordService.cs
  YouTubeApiService.cs
  AsyncJsonStore.cs                 # Thread-safe generic JSON persistence base class

Program.cs                          # DI composition root
Dockerfile
```

---

## Setup

### Prerequisites

- .NET 8 SDK
- Docker & docker-compose
- Discord Bot Token (with `applications.commands` and `bot` scopes)
- YouTube Data API v3 Key

### Environment Variables

Create `.env` next to `docker-compose.yml`:

```env
BotConfiguration__DiscordToken=YOUR_DISCORD_TOKEN
BotConfiguration__YoutubeApiKey=YOUR_YOUTUBE_API_KEY
```

### Configuration

Edit `appsettings.json` to set channel IDs, YouTube channel ID, and game list. Each game entry:

```json
{
  "Name": "Wuthering Waves",
  "Emoji": "🌊",
  "AffiliateLink": "https://...",
  "DiscountPercent": 10,
  "TopUpType": "LDShop login-assisted",
  "LootbarGameSeo": "wuthering-waves",
  "LootbarAffiliateLink": "https://...",
  "LootbarAppServiceId": 89
}
```

### Run Locally

```bash
dotnet restore
dotnet run
```

### Docker (Production)

```bash
docker compose up --build -d
docker compose logs -f
```

---

## Slash Commands

| Command | Permission | Description |
|---|---|---|
| `/refreshshop` | Administrator | Force-refresh both shop embeds in-place (no new messages created) |

---

## CI/CD

GitHub Actions runs on every push to `main`: restore → build Release → validate. Docker image is built on the VPS from source via `docker compose up --build`.

---

## Notable Implementation Details

- **Timezone-aware scheduling**: next refresh is calculated in Vietnam Standard Time (UTC+7) using `TimeZoneInfo`, correctly handling midnight boundary.
- **Lazy IEnumerable guard**: provider list is materialized with `.ToList()` before `Task.WhenAll` — without this, LINQ deferred execution silently skipped providers on the first run.
- **Async ref workaround**: `UpsertMessageAsync` returns `(bool changed, ulong messageId)` instead of `ref ulong` — async methods cannot have `ref` parameters in C#.
- **Resilience**: all external HTTP calls go through Polly's `AddStandardResilienceHandler` — automatic retries with exponential backoff and circuit breaking on transient failures.

---

## License

MIT
