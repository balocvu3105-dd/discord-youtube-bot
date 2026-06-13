# YouTube Discord Bot

A production-ready Discord bot built with **.NET 8** that automatically sends notifications to Discord channels when a YouTube channel uploads a new video or goes live. Also tracks game top-up promotions from LDShop.

> **Discord bot tự động thông báo** khi kênh YouTube đăng video mới hoặc livestream.  
> Tích hợp theo dõi khuyến mãi nạp game LDShop.

[![Build](https://github.com/balocvu3105-dd/discord-youtube-bot/actions/workflows/dotnet.yml/badge.svg)](https://github.com/balocvu3105-dd/discord-youtube-bot/actions/workflows/dotnet.yml)
![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet)
![Docker](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker)
![License](https://img.shields.io/badge/License-MIT-green)

---

## Features / Tính năng

### YouTube Notifications
- Detect new video uploads and send embeds to Discord
- Detect livestream start and send live alerts
- Prevent duplicate notifications across bot restarts
- Persistent state storage

### LDShop Tracking
- Track game top-up promotions automatically
- Refresh shop data on schedule
- `/refreshshop` slash command for manual refresh
- Config-driven game list

### Reliability
- Background worker services
- Restart-safe state handling via JSON persistence
- Structured logging with Serilog
- Docker-ready deployment

---

## Tech Stack

- .NET 8
- Discord.Net
- YouTube Data API v3
- Docker
- Serilog
- BackgroundService Workers

---

## Project Structure / Cấu trúc

```
Background/     → background worker services
Commands/       → slash commands
Config/         → configuration models
Models/         → data models
Services/       → business logic
data/           → runtime state storage (git-ignored)
logs/           → runtime logs (git-ignored)
```

---

## Setup / Cài đặt

### Clone

```bash
git clone https://github.com/balocvu3105-dd/discord-youtube-bot.git
cd discord-youtube-bot
```

### Environment Variables

Create `.env`:

```env
BotConfiguration__DiscordToken=YOUR_DISCORD_TOKEN
BotConfiguration__YoutubeApiKey=YOUR_YOUTUBE_API_KEY
```

### appsettings.json

```json
{
  "BotConfiguration": {
    "YoutubeChannelId": "YOUR_CHANNEL_ID",
    "VideoChannelId": 123456789,
    "LiveChannelId": 123456789
  }
}
```

### Run locally

```bash
dotnet restore
dotnet run
```

### Docker

```bash
docker build -t youtube-discord-bot .
docker run --env-file .env youtube-discord-bot
```

---

## Slash Commands

| Command | Description |
|---|---|
| `/refreshshop` | Force refresh LDShop promotions |

---

## State Files

Runtime state is persisted in:

```
data/last_video_state.json   → last known video ID
data/live_state.json         → current livestream state
```

These files are git-ignored and managed automatically.

---

## Production Notes

- Persistent state prevents duplicate notifications across restarts
- Restart-safe recovery via JSON state files
- Docker-ready, config-driven architecture

---

## Future Plans

- Database support (replace JSON persistence)
- Multi-channel notification system
- Telegram adapter
- Web dashboard
- Webhook support

---

## License

MIT