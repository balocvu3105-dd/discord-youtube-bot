# YouTube Discord Bot

Production-ready Discord bot built with .NET for:

- YouTube video notifications
- YouTube livestream alerts
- LDShop promo tracking
- Persistent runtime state
- Background services
- Docker deployment

---

# Features

## YouTube Notifications

- Detect new uploads
- Detect livestreams
- Auto send embeds to Discord channels
- Prevent duplicate notifications
- Persistent state storage

## LDShop Tracking

- Track game top-up promotions
- Refresh shop data automatically
- Slash command refresh support
- Config-driven game list

## Reliability

- Background workers
- JSON persistence
- Logging support
- Docker support
- Restart-safe state handling

---

# Tech Stack

- .NET 8
- Discord.Net
- YouTube Data API v3
- Docker
- Serilog
- BackgroundService Workers

---

# Project Structure


Background/     -> background workers
Commands/       -> slash commands
Config/         -> configuration models
Models/         -> data models
Services/       -> business logic/services
data/           -> runtime state storage
logs/           -> runtime logs


---

# Setup

## Clone Repository


git clone https://github.com/balocvu3105-dd/discord-youtube-bot.git
cd discord-youtube-bot

---

# Environment Variables

Create `.env`


BotConfiguration__DiscordToken=YOUR_DISCORD_TOKEN
BotConfiguration__YoutubeApiKey=YOUR_YOUTUBE_API_KEY


---

# appsettings.json


{
  "BotConfiguration": {
    "YoutubeChannelId": "YOUR_CHANNEL_ID",
    "VideoChannelId": 123456789,
    "LiveChannelId": 123456789
  }
}


---

# Run Locally


dotnet restore
dotnet run


---

# Docker

## Build


docker build -t youtube-discord-bot .


## Run


docker run --env-file .env youtube-discord-bot


---

# Slash Commands

## Refresh Shop


/refreshshop


Force refresh LDShop promotions.

---

# State Files

Runtime state is stored in:


data/last_video_state.json
data/live_state.json


These files are ignored by git.

---

# Logging

Logs are written to:


logs/


---

# Production Notes

- Uses persistent state to prevent duplicate notifications
- Supports restart-safe recovery
- Docker-ready deployment
- Config-driven architecture

---

# Future Plans

- Telegram adapter
- Web dashboard
- Multi-channel notification system
- Database support
- Premium features
- Webhook support

---

# License
