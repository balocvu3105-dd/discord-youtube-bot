# 🚀 Discord YouTube & Shop Automation Platform

A high-concurrency, production-ready event automation and notification engine built with **.NET 8 (C#)** and **ASP.NET Core Generic Host**, deployed on **Ubuntu Linux VPS via Docker**.

> **Engineering Objective:** Designed to solve real-world community synchronization challenges across multiple platforms (**YouTube, Twitch, Kick, Facebook Live, TikTok**) and gaming affiliate storefronts (**LDShop, Lootbar**). The platform focuses on **zero-duplicate notification guarantees**, **idempotent state recovery**, **strict concurrency control**, and **resilient third-party API orchestration**.

---

## 🏛 System Architecture & Topology

```text
       ┌────────────────────────────────────────────────────────┐
       │                   Discord Community                    │
       └───────────────────────────▲────────────────────────────┘
                                   │
       ┌───────────────────────────┴────────────────────────────┐
       │              Discord.Net Interaction Gateway           │
       │    (Command Modules, Event Handlers & Embed Builder)   │
       └───────────────────────────▲────────────────────────────┘
                                   │
       ┌───────────────────────────┴────────────────────────────┐
       │                 Domain & Service Layer                 │
       │  ┌─────────────────────┐       ┌────────────────────┐  │
       │  │ StreamerManager     │       │ ShopDiscount       │  │
       │  │ Service             │       │ Aggregator         │  │
       │  └──────────▲──────────┘       └─────────▲──────────┘  │
       └─────────────┼────────────────────────────┼─────────────┘
                     │                            │
       ┌─────────────┴─────────────┐┌─────────────┴─────────────┐
       │  IStreamPlatformProvider  ││   IShopDiscountProvider   │
       │ (Twitch, Kick, Facebook)  ││    (LDShop, Lootbar)      │
       └─────────────▲─────────────┘└─────────────▲─────────────┘
                     │                            │
       ┌─────────────┴────────────────────────────┴─────────────┐
       │         IHttpClientFactory + Polly Resilience          │
       │      (Circuit Breaker, Exponential Backoff, Retry)     │
       └───────────────────────────▲────────────────────────────┘
                                   │
       ┌───────────────────────────┴────────────────────────────┐
       │              Atomic Persistence Layer (JSON)           │
       │  (AsyncJsonStore<T>, SemaphoreSlim Mutex, .tmp Move)   │
       └────────────────────────────────────────────────────────┘
```

### High-Level Data Flow
1. **Event Ingestion & Scheduling**: Hosted worker loops (`YouTubeCheckerBackgroundService`, `UnifiedStreamCheckerBackgroundService`, `TikTokCheckerBackgroundService`, and `ShopBackgroundService`) execute non-blocking polling cycles independent of the `DiscordSocketClient` gateway event loop.
2. **Provider & Aggregation Layer**: Polling services query domain-specific interfaces (`IStreamPlatformProvider`, `IShopDiscountProvider`, `IYouTubeApiService`), decoupling core orchestration from individual external APIs.
3. **Resilience & Fault Tolerance**: All HTTP communications flow through `IHttpClientFactory` clients fortified with **Polly** resilience pipelines (circuit breakers, exponential backoff, connection pooling, and automatic retries).
4. **State Idempotency & Concurrency Guard**: Events are evaluated against in-memory snapshots (`BotState`, `_liveStates`) and atomically persisted via `AsyncJsonStore<T>` (`SemaphoreSlim` mutexes + `.tmp` file renaming) to ensure zero duplicate alerts across container or OS restarts.
5. **Discord Gateway Dispatch**: Verified state transitions trigger rich embed formatting, role mentions (`@everyone`/`@here`), or in-place embed updates (`ModifyAsync`) via `Discord.Net`.

---

## ⚙ Core Engineering Design & Patterns

### 1. Extensible Provider Pattern (Open/Closed Principle)
Instead of monolithic API scraping classes, the application decouples external providers through clean domain contracts:
- **Affiliate Shop Aggregation (`IShopDiscountProvider`)**: Implemented independently by `LdShopDiscountProvider` and `LootbarDiscountProvider`. The `ShopDiscountAggregator` queries all registered providers in parallel via dependency injection (`IEnumerable<IShopDiscountProvider>`), standardizing disparate JSON models into a unified `ShopDiscount` domain entity without modifying core logic when new storefronts are integrated.
- **Multi-Platform Stream Tracking (`IStreamPlatformProvider`)**: `TwitchService`, `KickService`, and `FacebookLiveService` implement a shared interface, allowing `StreamerManagerService` and slash commands (`/live add`, `/live status`) to dynamically manage streamers across platforms with unified status evaluation.

### 2. Concurrency Safety & Idempotent State Recovery
In distributed and scheduled automation systems, duplicate notifications are the most common failure mode during network interruptions or process restarts. This platform implements multi-layer state idempotency:
- **In-Memory State Snapshotting (`SyncStateOnStartupAsync`)**: `YouTubeCheckerBackgroundService` loads state (`last_video_state.json`, `live_state.json`) exactly once during application initialization. Subsequent checks operate against memory-safe state dictionaries (`_botState`, `_liveStates`), preventing I/O race conditions where disk reads every 2 minutes could revert state and trigger duplicate notifications.
- **Atomic File Persistence (`AsyncJsonStore<T>`)**: All persistence operations (`PersistenceService`, `LiveStateService`, `ShopMessagePersistenceService`) inherit from `AsyncJsonStore<T>`, utilizing a `SemaphoreSlim(1, 1)` async mutex to serialize coroutine access. To guarantee crash-consistency, writes occur to a `.tmp` file first before performing an OS-level atomic file move (`File.Move(..., overwrite: true)`), ensuring JSON corruption never occurs during abrupt container shutdowns or power loss.
- **Message Reference Tracking (`IShopMessagePersistenceService`)**: The shop service tracks existing Discord message IDs across channels (`shop_messages.json`). Upon scheduled updates (`00:00` and `12:00` UTC+7), the engine performs **in-place message edits (`ModifyAsync`)** instead of sending duplicate messages, preserving channel cleanliness and chat context.

### 3. Non-Blocking Background Processing & Dual DI Registration
To prevent blocking the critical Discord gateway WebSocket thread (`DiscordSocketClient.InteractionCreated`), background tasks run inside dedicated `IHostedService` (`BackgroundService`) workers:
- **`ShopBackgroundService` Dual-Registration**: Registered as *both* a `Singleton` and an `IHostedService`. This architectural pattern allows `ShopCommandModule` (`/shop refresh`) to inject the exact same running service instance to trigger immediate manual updates while respecting internal synchronization mutexes (`SemaphoreSlim _refreshLock = new(1, 1)`) against fixed timezone schedules (`SE Asia Standard Time`).
- **Subprocess Isolation (`ProcessStartInfo.ArgumentList`)**: For platforms requiring complex WebSocket/Protobuf handling (`TikTokLive`), `TikTokService` orchestrates an isolated Python subprocess (`tiktok_check.py`). To prevent OS-level Argument Injection, string concatenation is strictly forbidden; all streamer usernames and arguments are passed via `.NET Core's ProcessStartInfo.ArgumentList` parameter array.

### 4. Network Resilience & Defensive HTTP Engineering
External APIs (YouTube Data API v3, Twitch, Kick, LDShop, Lootbar) are subject to rate limits, Cloudflare challenges, and unexpected latency:
- **`IHttpClientFactory` Connection Pooling**: Eliminates socket exhaustion (`TIME_WAIT` leaks) by centralizing HTTP client configuration and pooling underlying `HttpMessageHandler` instances.
- **Polly Resilience Pipelines**: Primary HTTP clients incorporate `.AddStandardResilienceHandler()`, automatically applying:
  - **Circuit Breakers**: Temporarily halts requests to degraded external APIs to prevent cascading timeouts.
  - **Exponential Backoff with Jitter**: Smooths out retry spikes when third-party servers drop connections.
  - **Custom Request Headers**: Configures proper `User-Agent`, `Referer`, and compression headers (`GZip | Deflate`) to navigate strict provider CDN requirements.
- **Defensive URI Encoding**: All dynamic routing arguments and queries (`Uri.EscapeDataString`) are sanitized to neutralize Path Traversal (`../`) attacks across file paths and REST endpoints.

---

## 🛠 Domain Modules & Capabilities

| Module | Core Responsibilities | Key Technical Highlights |
| :--- | :--- | :--- |
| **YouTube Module** | Video upload & livestream lifecycle monitoring (`Start`, `End`, `VOD transition`). | Quota-optimized API polling, duplicate notification suppression (`TerminalStatuses`), role mentions. |
| **Multi-Platform Streams** | Real-time stream tracking for **Twitch, Kick, Facebook Live, TikTok**. | Dynamic management via `/live` slash commands (`add`, `remove`, `status`, `check`), unified embed formatting. |
| **Affiliate Shop Module** | Daily automated discount aggregation & price drop notifications (`LDShop`, `Lootbar`). | Provider pattern aggregation, in-place embed updates (`ModifyAsync`), UTC+7 timezone scheduling (`00:00`, `12:00`). |
| **Discord Gateway** | Interaction handling (`Slash Commands`), global registration, structured error boundaries. | Async event exception trapping (`Ready`, `InteractionCreated`), decoupled `SocketInteractionContext`. |

---

## 💻 Tech Stack & Infrastructure

* **Runtime & Framework:** `.NET 8`, `ASP.NET Core Generic Host` (`Host.CreateApplicationBuilder`)
* **Language:** `C# 12` (Async/Await, Records, Pattern Matching, Nullable Reference Types)
* **Discord Integration:** `Discord.Net v3.x` (`DiscordSocketClient`, `InteractionService`)
* **Resilience & HTTP:** `Microsoft.Extensions.Http.Resilience` (`Polly`), `IHttpClientFactory`
* **Logging & Observability:** `Serilog` (Structured JSON rolling daily file logs `logs/bot-.log` + Console sink)
* **Configuration & Environment:** `DotNetEnv`, `IOptions<BotConfiguration>` with DataAnnotation validation on startup
* **Containerization & CI/CD:** `Docker`, `Docker Compose` multi-stage build, `GitHub Actions` deployment pipelines (`deploy.ps1`, `.bat` automation for Ubuntu VPS)

---

## 🚀 Local Development & Setup

### Prerequisites
* **.NET 8.0 SDK** or later
* **Docker & Docker Compose** (optional, for containerized execution)
* **Discord Bot Token** (with `Guilds` and `GuildMessages` Gateway Intents enabled in Discord Developer Portal)
* **YouTube Data API v3 Key**

### Configuration (`appsettings.json` / `.env`)
Create an `.env` file or configure `appsettings.json` with required credentials:
```json
{
  "BotConfiguration": {
    "DiscordToken": "YOUR_DISCORD_BOT_TOKEN",
    "YouTubeApiKey": "YOUR_YOUTUBE_API_KEY",
    "NotificationChannelId": 123456789012345678,
    "ShopNotificationChannelId": 123456789012345678,
    "CheckIntervalSeconds": 120
  }
}
```

### Running Locally
```bash
# Clone repository
git clone https://github.com/balocvu3105-dd/discord-youtube-bot.git
cd discord-youtube-bot

# Run via .NET CLI
dotnet build
dotnet run

# OR Run via Docker Compose
docker compose up --build -d
```

---

## 📊 Operational & Production Experience

This platform is actively deployed on an **Ubuntu Linux VPS** serving a live Discord gaming community. Production operational practices include:
- **Zero-Downtime Container Deployments**: Automated build and push workflows (`Dockerfile`, `deploy.ps1`) supporting environment variable injection (`$env:VPS_IP`, `$env:VPS_USER`) to secure infrastructure credentials.
- **Structured Error Logging**: Comprehensive exception capture inside Serilog daily rolling logs (`bot-YYYY-MM-DD.log`), retaining 7 days of diagnostic history while filtering noisy framework logs (`LogEventLevel.Warning`).
- **Post-Mortem & Reliability Fixes**: Continuous architectural refinement based on real-world edge cases (such as isolating memory state during background checks to resolve recurring 2-minute Discord notification duplicates).

---

## 📷 System Screenshots

### YouTube & Livestream Notifications
<!-- Add screenshot of rich embed notifications -->

### Affiliate Shop Discount Aggregation (`/shop refresh`)
<!-- Add screenshot of shop embed tables -->

### Streamer Management Commands (`/live status`, `/live check`)
<!-- Add screenshot of slash command interactions -->

---

## 👨‍💻 Author

**Bá Lộc Vũ (DynamiteV)**  
*Backend Developer (.NET / C# / ASP.NET Core / Clean Architecture)*  
* **GitHub:** [https://github.com/balocvu3105-dd](https://github.com/balocvu3105-dd)
