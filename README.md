<div align="center">

# 🌍 GlobalJobHunter

**AI-powered job hunting bot that runs 24/7 — finds, scores, and delivers .NET jobs straight to your Telegram.**

[![CI/CD](https://github.com/zhran9/GlobalJobHunter-/actions/workflows/deploy.yml/badge.svg)](https://github.com/zhran9/GlobalJobHunter-/actions)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com)
[![Docker](https://img.shields.io/badge/Docker-ready-blue)](https://hub.docker.com)
[![Railway](https://img.shields.io/badge/Deployed-Railway-black)](https://railway.app)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

</div>

---

## 📌 What Is This?

GlobalJobHunter is a **.NET 8 Worker Service** that runs as a background daemon in the cloud. Every **4 hours** it:

1. Scrapes **8 job platforms** in parallel
2. Filters jobs posted in the last 48 hours
3. Sends each job to an **LLM (Llama 3.3 70B via Groq)** for relevance scoring (0–100)
4. Pushes a **Telegram alert** for every job scoring above 65

Zero manual work. Set it up once, receive relevant jobs forever.

---

## 📸 Screenshots

| Telegram Alert | Bot Commands |
|---|---|
| ![Job Alert](docs/screenshots/alert.png) | ![Bot Commands](docs/screenshots/commands.png) |

> **Example alert received:**
> ```
> 💼 Senior .NET Backend Engineer
> 🏢 Acme Corp
> 📍 Remote
> 🔗 Source: LinkedIn
> 🔗 Apply: https://linkedin.com/jobs/...
> ```

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Every 4 Hours                        │
│                                                         │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐  │
│  │ LinkedIn │  │Remotive  │  │Arbeitnow │  │Wuzzuf  │  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └───┬────┘  │
│       │              │              │             │       │
│  ┌────┴──────────────┴──────────────┴─────────────┴───┐  │
│  │              Worker (Strategy Pattern)              │  │
│  │         Fetches all providers in parallel           │  │
│  └─────────────────────────┬───────────────────────────┘  │
│                             │                           │
│                    ┌────────▼────────┐                  │
│                    │  Recency Filter │ (last 48h only)  │
│                    └────────┬────────┘                  │
│                             │                           │
│                    ┌────────▼────────┐                  │
│                    │  Deduplication  │ (SQLite batch)   │
│                    └────────┬────────┘                  │
│                             │                           │
│                    ┌────────▼────────┐                  │
│                    │  Groq LLM API   │ score 0–100      │
│                    │  Llama 3.3 70B  │                  │
│                    └────────┬────────┘                  │
│                             │ score ≥ 65                │
│                    ┌────────▼────────┐                  │
│                    │ Telegram Alerts │ multi-user       │
│                    └─────────────────┘                  │
└─────────────────────────────────────────────────────────┘
```

---

## 🔌 Job Sources (8 Platforms)

| Provider | Method | Focus |
|---|---|---|
| **LinkedIn** | Unauthenticated guest API | Global .NET jobs |
| **Remotive** | REST API | Remote software jobs |
| **WeWorkRemotely** | RSS feed | Remote back-end jobs |
| **Wuzzuf** | HTML scraping | Egypt / MENA region |
| **Arbeitnow** | REST API | Global remote jobs |
| **RemoteOK** | REST API | Remote C# jobs |
| **Jobicy** | REST API | Remote .NET jobs |
| **WorkingNomads** | REST API | Remote back-end jobs |

---

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 8 Worker Service |
| **Architecture** | Strategy Pattern · Dependency Injection · `BackgroundService` |
| **Database** | Entity Framework Core 8 + SQLite |
| **AI / LLM** | Groq API — Llama 3.3 70B (OpenAI-compatible endpoint) |
| **Messaging** | Telegram Bot API (`Telegram.Bot` v22) |
| **Web Scraping** | `System.Net.Http` · AngleSharp · Playwright (headless Chromium) |
| **Containerization** | Docker (multi-stage build) |
| **CI/CD** | GitHub Actions → Docker Hub → Railway deploy hook |
| **Cloud** | Railway (persistent volume for SQLite) |
| **Config** | `IOptions<T>` + environment-specific `appsettings` + Railway Variables |

---

## 📁 Project Structure

```
GlobalJobHunter.Service/
│
├── Providers/                  # 8 job source scrapers (Strategy Pattern)
│   ├── IJobProvider.cs         # Common interface
│   ├── LinkedInProvider.cs
│   ├── RemotiveProvider.cs
│   ├── WeWorkRemotelyProvider.cs
│   ├── WuzzufProvider.cs
│   ├── IndeedProvider.cs       # → Arbeitnow API
│   ├── WellfoundProvider.cs    # → RemoteOK API
│   ├── OttaProvider.cs         # → Jobicy API
│   └── HimalayasProvider.cs   # → WorkingNomads API
│
├── Services/
│   ├── GroqEvaluatorService.cs      # LLM scoring via Groq API
│   ├── TelegramNotifierService.cs   # Broadcast to all registered users
│   └── TelegramBotWorker.cs         # /start /stop /status commands
│
├── Data/
│   └── AppDbContext.cs         # EF Core context (JobRecords + AppUsers)
│
├── Models/
│   ├── JobPosting.cs           # Raw scraped job
│   ├── JobRecord.cs            # DB entity with AI score
│   └── AppUser.cs              # Registered Telegram users
│
├── Worker.cs                   # Main 4h cycle orchestrator
├── Program.cs                  # DI setup + DB migration
├── Dockerfile                  # Multi-stage build
├── docker-compose.yml          # Local dev with volume
└── .github/workflows/
    └── deploy.yml              # CI/CD pipeline
```

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Groq API key](https://console.groq.com) (free)
- [Telegram Bot token](https://t.me/BotFather) (free)

### Local Development

```bash
git clone https://github.com/zhran9/GlobalJobHunter-.git
cd GlobalJobHunter-
```

Create `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=jobs.db"
  },
  "Telegram": {
    "BotToken": "YOUR_BOT_TOKEN",
    "ChatId":   "YOUR_CHAT_ID"
  },
  "Ai": {
    "GroqApiKey": "YOUR_GROQ_KEY"
  }
}
```

```bash
dotnet run
```

### Docker

```bash
docker compose up -d
```

### Deploy to Railway

1. Fork this repo
2. Create a new Railway project → deploy from GitHub
3. Set environment variables in Railway dashboard:
   - `Telegram__BotToken`
   - `Telegram__ChatId`
   - `Ai__GroqApiKey`
4. Add a Volume mounted at `/app/data`
5. Every push to `main` auto-deploys via GitHub Actions ✅

---

## 🤖 Telegram Bot Commands

| Command | Description |
|---|---|
| `/start` | Register to receive job alerts |
| `/stop` | Unsubscribe from alerts |
| `/status` | Show stats (total users, jobs found) |

---

## ⚙️ How the AI Scoring Works

Each job is evaluated by **Llama 3.3 70B** with a prompt that checks:
- Is this a .NET / C# role?
- Is the seniority level appropriate (mid/senior)?
- Is it remote-friendly or Egypt-based?
- Does the company seem legitimate?

The model returns a **score (0–100)** and `isMatch` boolean. Only jobs with **score ≥ 65** generate a Telegram alert. Everything is saved to SQLite regardless of score for deduplication on the next cycle.

---

## 📊 CI/CD Pipeline

```
git push → GitHub Actions
              │
              ├─ dotnet build (verify no errors)
              ├─ docker build (multi-stage)
              ├─ docker push → Docker Hub
              └─ curl Railway deploy hook → auto-redeploy ✅
```

---

## 📄 License

MIT © [Osama Zahran](https://github.com/zhran9)
