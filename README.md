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
- Monitors **multiple YouTube channels** simultaneously — each polled independently on a configurable interval
- Detects new video uploads and livestream start/end events via YouTube Data API v3
- Sends rich Discord embeds to dedicated channels with role pings and full deduplication
- Restart-safe: survives container restarts without re-sending notifications via persistent JSON state
- On restart, any channel already live triggers an immediate notification rather than being silently skipped

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
  ShopCommandModule.cs               # /refreshshop slash command (Admin only, cooldown 60s)

Config/
  BotConfiguration.cs                # Strongly-typed configuration model

Models/
  BotState.cs                        # Last video ID + last checked timestamp
  VideoInfo.cs                       # Video metadata + IsLive flag
  ShopGameConfig.cs                  # Per-game config (LDShop + Lootbar fields)
  ShopMessageState.cs                # Persisted Discord message IDs per provider
  LdShopPromo.cs                     # LDShop promo result (Equals/GetHashCode by name+discount)
  Models.cs                          # (legacy stub — content moved to files above)

Services/
  IServices.cs                       # Service & provider interfaces
  IShopDiscountProvider.cs           # Shop provider contract
  ShopService.cs                     # Builds Discord embeds for LDShop & Lootbar
  ShopDiscountAggregator.cs          # Composes IShopDiscountProvider list, runs in parallel
  LdShopDiscountProvider.cs
  LdShopDiscountService.cs           # LDShop HTTP API client (parallel WarmCache, CancellationToken)
  LootbarDiscountProvider.cs
  LootbarDiscountService.cs          # Lootbar HTTP API client (app_service_id mapping)
  LdShopScraperService.cs            # LDShop scraper (thread-safe _nameCache via SemaphoreSlim)
  ShopMessagePersistenceService.cs   # Persists Discord message IDs to JSON
  DiscordService.cs                  # Discord client, events, startup notification
  YouTubeApiService.cs               # YouTube Data API v3 wrapper
  LiveStateService.cs                # Tracks current live stream state
  PersistenceService.cs              # Last-video-state persistence
  AsyncJsonStore.cs                  # Thread-safe generic JSON persistence base class

Program.cs                           # DI composition root
Dockerfile
appsettings.json                     # ⚠️ GITIGNORED — chứa token/key, tạo thủ công trên VPS
appsettings.example.json             # Template đầy đủ để tham khảo
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

`appsettings.json` bị **gitignore** (chứa token/key nhạy cảm). Tạo file này thủ công trên VPS hoặc local dựa theo `appsettings.example.json`.

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information" },
    "Console": {
      "FormatterName": "simple",
      "FormatterOptions": { "TimestampFormat": "[HH:mm:ss] " }
    }
  },
  "BotConfiguration": {
    "DiscordToken":   "YOUR_DISCORD_BOT_TOKEN",
    "YoutubeApiKey":  "YOUR_YOUTUBE_API_KEY",
    "YoutubeChannelIds": [
      "UCxxxxxxxxxxxxxxxxxxxxxxxx",
      "UCyyyyyyyyyyyyyyyyyyyyyyyyyy"
    ],

    "LiveChannelId":   1234567890123456789,
    "VideoChannelId":  1234567890123456789,
    "ShopChannelId":   1234567890123456789,
    "StatusChannelId": 0,

    "LiveRoleId":  1234567890123456789,
    "VideoRoleId": 1234567890123456789,

    "CheckIntervalSeconds": 120,

    "StateFilePath":     "data/last_video_state.json",
    "LiveStateFilePath": "data/live_state.json",

    "ShopNotice": "",

    "LootbarShopCode": "YourShopCode",
    "LootbarShopLink": "https://www.lootbar.com/shop/YourShopCode",

    "ShopGames": [
      {
        "Name":         "Wuthering Waves",
        "Emoji":        "🌊",
        "CommodityId":  10016,
        "SkuLabelId":   74,
        "AffiliateLink": "https://chain.ldshop.gg/YOUR_LINK",
        "DiscountPercent": 10,
        "PromoNote":    "Tiết kiệm ngay khi nạp Lunite!",
        "TopUpType":    "LDShop đăng nhập hộ",
        "Warning":      "⚠️ Không đăng nhập game trong lúc đang xử lý",
        "LootbarGameSeo":          "wuthering-waves",
        "LootbarAffiliateLink":    "https://www.lootbar.com/top-up/wuthering-waves?shop_code=YourCode",
        "LootbarFallbackDiscount": 0,
        "LootbarAppServiceId":     89
      }
    ]
  }
}
```

#### Bảng field bắt buộc / tuỳ chọn

| Field | Bắt buộc | Mô tả |
|---|:---:|---|
| `DiscordToken` | ✅ | Bot token từ Discord Developer Portal |
| `YoutubeApiKey` | ✅ | YouTube Data API v3 key |
| `YoutubeChannelIds` | ✅ | Mảng ID kênh YouTube cần monitor (`UC...`), hỗ trợ nhiều kênh |
| `LiveChannelId` | ✅ | Channel Discord nhận thông báo livestream |
| `VideoChannelId` | ✅ | Channel Discord nhận thông báo video mới |
| `ShopChannelId` | ✅ | Channel Discord để đăng shop embed |
| `StatusChannelId` | ⬜ | Channel nhận thông báo bot khởi động/restart. `0` = tắt |
| `LiveRoleId` | ⬜ | Role ping khi livestream bắt đầu. `0` = không ping |
| `VideoRoleId` | ⬜ | Role ping khi có video mới. `0` = không ping |
| `CheckIntervalSeconds` | ⬜ | Tần suất poll YouTube API (giây, mặc định `120`) |
| `ShopNotice` | ⬜ | Dòng thông báo phụ hiển thị trên shop embed. `""` = ẩn |
| `LootbarShopCode` | ✅* | Mã shop affiliate Lootbar (ví dụ: `CataWuwa`) |
| `LootbarShopLink` | ✅* | Link trang shop Lootbar chính |
| `ShopGames[].Name` | ✅ | Tên game hiển thị trên embed |
| `ShopGames[].Emoji` | ⬜ | Emoji đầu tên game |
| `ShopGames[].CommodityId` | ✅ | ID game trên LDShop (dùng để fetch % giảm giá realtime) |
| `ShopGames[].SkuLabelId` | ✅ | SKU label ID trên LDShop |
| `ShopGames[].AffiliateLink` | ✅ | Link affiliate LDShop cho game này |
| `ShopGames[].DiscountPercent` | ⬜ | % giảm giá fallback nếu API LDShop không trả về (mặc định `0`) |
| `ShopGames[].PromoNote` | ⬜ | Ghi chú promo hiển thị dưới tên game |
| `ShopGames[].TopUpType` | ⬜ | Loại nạp (ví dụ: "Nạp UID", "đăng nhập hộ") |
| `ShopGames[].Warning` | ⬜ | Cảnh báo hiển thị trên embed (ví dụ: đổi mật khẩu sau khi nạp) |
| `ShopGames[].LootbarGameSeo` | ✅* | SEO slug của game trên Lootbar (ví dụ: `wuthering-waves`) |
| `ShopGames[].LootbarAffiliateLink` | ✅* | Link affiliate Lootbar cho game này |
| `ShopGames[].LootbarFallbackDiscount` | ⬜ | % fallback nếu Lootbar API không trả về (mặc định `0`) |
| `ShopGames[].LootbarAppServiceId` | ✅* | `app_service_id` của game trên Lootbar (dùng để fetch % realtime) |

> ✅* = Bắt buộc nếu dùng Lootbar integration.

> ⚠️ **Lưu ý VPS**: sau khi thay đổi `appsettings.json` trực tiếp trên VPS, cần rebuild image để thay đổi có hiệu lực:
> ```bash
> cd /root/bot/discord-youtube-bot
> docker compose up --build -d
> ```

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
- **Multi-channel YouTube monitoring**: `YoutubeChannelIds` is an array — each channel is polled independently per tick, with per-channel `LastVideoId` tracking. Legacy single-channel state is auto-migrated on first boot.
- **Startup live re-notification**: on restart, if a channel is actively live and not yet notified, the startup sync sends the notification immediately rather than silently marking it `live_notified` and skipping it.
- **Resilience**: all external HTTP calls go through Polly's `AddStandardResilienceHandler` — automatic retries with exponential backoff and circuit breaking on transient failures.
- **Structured logging**: Serilog writes JSON-structured logs to both console and daily rolling files (7-day retention), with Discord and Microsoft noise suppressed to `Warning` level.

---

## License

MIT
