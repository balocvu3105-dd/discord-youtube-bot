# 🚀 Discord YouTube & Shop Bot

Production-oriented community automation platform built with .NET 8.

This project automates YouTube notifications, affiliate shop promotions and scheduled content synchronization for Discord communities. The system integrates multiple external APIs, runs background processing jobs and is deployed on a Linux VPS using Docker.

> Developed as a real-world backend project to demonstrate software engineering, API integration, background processing and production deployment skills.

---

# 📌 Overview

Managing gaming communities often requires multiple tools:

* Monitoring YouTube uploads and livestreams
* Managing affiliate shop promotions
* Updating discount information
* Avoiding duplicate notifications
* Running scheduled tasks

This project combines these responsibilities into a single automated platform.

The bot continuously monitors YouTube channels, tracks livestream status, aggregates discount information from multiple game shop providers and publishes updates directly to Discord.

---

# ✨ Project Highlights

* Built with .NET 8 and C#
* Deployed on Ubuntu VPS using Docker
* Multi-platform livestream tracking (YouTube, Twitch, Kick, Facebook, TikTok)
* Security-hardened (Argument injection & Path traversal defense)
* Integrates external APIs
* Multi-provider architecture
* Background scheduled services
* Persistent state management
* Structured logging
* GitHub Actions CI/CD
* Production deployment

---

# 🛠 Core Features

## Multi-Platform Livestream Tracking & YouTube Monitoring

Automatically monitors multiple platforms and channels, detecting:

* New video uploads (YouTube)
* Livestream starts/ends across **YouTube, Twitch, Kick, Facebook, and TikTok**

Features:

* Multi-platform & Multi-channel support
* Dynamic streamer management via `/live` slash commands (`add`, `remove`, `status`, `check`)
* Role mentions & Rich Discord embeds
* Duplicate notification prevention & restart-safe state recovery
* Security hardened against Argument Injection and Path Traversal

---

## Affiliate Shop Automation

Aggregates discount information from multiple providers.

Current providers:

* LDShop
* Lootbar

Features:

* Scheduled refresh
* Manual refresh command
* Persistent message tracking
* In-place embed updates
* Multi-provider support

---

## Background Processing

Implemented using .NET Hosted Services.

Responsibilities:

* Polling YouTube APIs
* Monitoring livestream status
* Refreshing shop information
* State synchronization

---

## Persistent State Management

Stores application state to prevent duplicated events after restart.

Examples:

* Last processed video
* Current livestream state
* Shop message identifiers

---

# 🏗 Architecture

## High-Level Architecture

```text
Discord Community
        │
        ▼
Discord Bot Layer
        │
 ┌──────┴──────┐
 ▼             ▼
YouTube Module Shop Module
 ▼             ▼
External APIs
        │
        ▼
Persistence Layer
```

---

## Architecture Decisions

### Provider Pattern

The shop system follows the Provider Pattern.

Each provider implements:

```csharp
public interface IShopDiscountProvider
{
    Task<IEnumerable<ShopDiscount>> GetDiscountsAsync();
}
```

Benefits:

* Extensible architecture
* Open/Closed Principle
* Easy integration of new providers

---

### Background Services

The system uses:

```csharp
BackgroundService
IHostedService
```

to execute scheduled jobs without blocking Discord interactions.

Benefits:

* Separation of concerns
* Better scalability
* Improved maintainability

---

### IHttpClientFactory

All external APIs are accessed through:

```csharp
IHttpClientFactory
```

Benefits:

* Connection pooling
* Reduced socket exhaustion
* Centralized configuration
* Better resiliency

---

# ⚙ Technology Stack

## Backend

* C#
* .NET 8
* ASP.NET Core Generic Host

## Discord

* Discord.Net

## External Integrations

* YouTube Data API v3
* LDShop API
* Lootbar API

## Infrastructure

* Docker
* Docker Compose
* Ubuntu Linux VPS

## Logging

* Serilog

## Reliability

* Polly Resilience Pipeline

## CI/CD

* GitHub Actions

---

# 🚧 Technical Challenges

## Multi-Channel Monitoring

### Problem

Need to monitor multiple YouTube channels independently while preventing duplicate notifications.

### Solution

Implemented per-channel state tracking and persistence to maintain synchronization across restarts.

---

## External API Reliability

### Problem

Third-party APIs may timeout or fail unexpectedly.

### Solution

Implemented:

* Retry policies
* Exponential backoff
* Circuit breaker protection

using Polly resilience pipelines.

---

## Restart-Safe Notifications

### Problem

Application restarts could trigger duplicated Discord notifications.

### Solution

Persisted application state and restored it during startup to guarantee idempotent processing.

---

## Scheduled Synchronization

### Problem

Shop updates must occur at fixed times every day.

### Solution

Implemented timezone-aware scheduling using BackgroundService and Vietnam Standard Time calculations.

---

## Security Hardening & Defensive Coding

### Problem

Integrating external Python subprocesses (TikTokLive) and user-supplied streamer usernames/slugs creates potential attack vectors such as Argument Injection and URL Path Traversal.

### Solution

* Replaced string concatenation in subprocess execution with .NET Core's `ProcessStartInfo.ArgumentList` to guarantee OS-level argument escaping.
* Implemented strict URI encoding (`Uri.EscapeDataString`) when constructing external API requests and stream URLs.
* Refactored deployment automation (`deploy.ps1`) to support environment variable overrides (`$env:VPS_IP`, `$env:VPS_USER`), preventing sensitive server IP disclosure in public repositories.

---

# 📚 What I Learned

During development I gained practical experience with:

* .NET 8
* C#
* Dependency Injection
* SOLID Principles
* Provider Pattern
* Background Services
* IHttpClientFactory
* External API Integration
* Docker Deployment
* Linux VPS Administration
* Structured Logging
* CI/CD Pipelines
* Production Monitoring
* Troubleshooting Live Systems

---

# 🚀 Production Experience

The application is actively deployed and used by a real Discord gaming community.

Production responsibilities include:

* Monitoring uptime
* Managing Docker containers
* Debugging API failures
* Maintaining scheduled jobs
* Handling configuration updates
* Log analysis
* Production troubleshooting

---

# 💻 Local Development

## Requirements

* .NET 8 SDK
* Docker
* Discord Bot Token
* YouTube Data API Key

## Run

```bash
git clone https://github.com/balocvu3105-dd/discord-youtube-bot.git

cd discord-youtube-bot

docker compose up --build
```

---

# 🔮 Future Improvements

* Web Dashboard
* PostgreSQL Storage
* Redis Caching
* Metrics & Monitoring
* Prometheus Integration
* Grafana Dashboards
* Distributed Scheduling
* Multi-Community Management

---

# 📷 Screenshots

## YouTube Notifications

(Add Screenshot)

## Shop Automation

(Add Screenshot)

## Discord Commands

(Add Screenshot)

---

# 👨‍💻 Author

Bá Lộc Vũ (DynamiteV)

Backend Developer

Technologies:

* .NET
* ASP.NET Core
* PostgreSQL
* Docker
* Clean Architecture
* Discord API

GitHub:
https://github.com/balocvu3105-dd
