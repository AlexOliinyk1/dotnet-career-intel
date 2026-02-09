# CareerIntel - .NET Career Intelligence Platform

**AI-powered career intelligence system for .NET developers and architects seeking maximum compensation and career optimization.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/AlexOliinyk1/dotnet-career-intel)

---

## 🎯 What is CareerIntel?

CareerIntel is a comprehensive CLI tool that helps .NET developers and architects:

- 🔍 **Scrape job vacancies** from 21 platforms (Djinni, DOU, Dice, Remotive, Work.ua, etc.)
- 📊 **Analyze market trends** - skill demand, salary intelligence, career paths
- 🎯 **Match opportunities** against your profile with AI-powered scoring
- 📝 **Generate tailored resumes** and cover letters optimized for ATS
- 🧠 **Prepare for interviews** with inferred questions and learning plans
- 💰 **Negotiate offers** with data-driven strategies
- 📈 **Track competitiveness** and get actionable feedback
- 🤖 **Auto-apply** to matching positions with intelligent filtering

Built for developers seeking **$120-250/hr B2B contracts** or **$150K-400K/year employment** through data-driven career optimization.

---

## 🚀 Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Windows, macOS, or Linux

### Installation

```bash
# Clone the repository
git clone https://github.com/AlexOliinyk1/dotnet-career-intel.git
cd dotnet-career-intel

# Build the solution
dotnet build

# Run the CLI
dotnet run --project src/CareerIntel.Cli
```

### First-Time Setup

1. **Create your profile:**
```bash
dotnet run --project src/CareerIntel.Cli -- profile create
```

2. **Scan job markets:**
```bash
dotnet run --project src/CareerIntel.Cli -- scan --platforms djinni,dou,justjoinit
```

3. **Analyze and match:**
```bash
dotnet run --project src/CareerIntel.Cli -- analyze
dotnet run --project src/CareerIntel.Cli -- match
```

4. **View dashboard:**
```bash
dotnet run --project src/CareerIntel.Cli -- dashboard
```

---

## 📚 Core Features

### 🔍 **Job Market Intelligence**

```bash
# Scrape from multiple platforms
career-intel scan --platforms djinni,dou,linkedin,justjoinit

# Analyze skill demand and trends
career-intel analyze

# Get salary intelligence
career-intel salary
```

**Supported Platforms:**
- 🇺🇦 Djinni.co, DOU.ua, Work.ua
- 🇵🇱 JustJoin.it, NoFluffJobs
- 🌍 LinkedIn, RemoteOk, WeWorkRemotely, Arc.dev, Wellfound
- 💼 HackerNews (Who is Hiring), Himalayas, Jobicy, Toptal, Built In

### 🎯 **Smart Matching & Scoring**

```bash
# Match vacancies against your profile
career-intel match --min-score 0.7

# Assess your competitiveness
career-intel assess
```

AI-powered scoring engine evaluates:
- Skill alignment (required, preferred, missing)
- Salary fit and negotiation potential
- Experience level match
- Cultural/remote work preferences
- Pass probability estimation

### 📝 **Resume & Application Tools**

```bash
# Generate tailored resume for a vacancy
career-intel resume --vacancy-id <id> --output resume.pdf

# Simulate ATS/recruiter/tech-lead review
career-intel simulate --vacancy-id <id>

# Auto-apply to matching positions
career-intel apply --auto-screen
```

Features:
- ATS-optimized formatting
- Keyword density analysis
- Cover letter generation
- Multi-reviewer simulation
- Application tracking

### 🧠 **Interview Preparation**

```bash
# Generate interview prep plan
career-intel interview-prep --vacancy-id <id>

# Analyze interview topics
career-intel topics

# Get learning resources
career-intel resources --skill "Azure" --skill "Kubernetes"

# Record and analyze feedback
career-intel feedback --company "Acme Corp" --outcome "Technical rejection"
career-intel insights
```

Provides:
- Inferred interview questions based on job description
- Pass probability analysis
- Personalized learning plans
- ROI-ranked skill gaps
- Pattern detection across interviews

### 💰 **Negotiation Intelligence**

```bash
# Analyze offer and generate strategy
career-intel negotiate --offer-amount 150000 --currency USD

# Compare with market data
career-intel salary --role "Solutions Architect" --region "US Remote"
```

### 📊 **Career Strategy**

```bash
# View unified dashboard
career-intel dashboard

# Get readiness assessment
career-intel readiness --target-vacancy <id>

# Generate portfolio project ideas
career-intel portfolio

# Discover Ukraine-friendly companies
career-intel companies
```

### 🔔 **Job Monitoring**

```bash
# Watch for new matching vacancies
career-intel watch --platforms djinni,dou --notify
```

Supports Telegram and email notifications (see [Configuration](#-configuration)).

---

## 🏗️ Architecture

```
CareerIntel/
├── src/
│   ├── CareerIntel.Core/          # Domain models and interfaces
│   ├── CareerIntel.Scrapers/      # 11 job board scrapers
│   ├── CareerIntel.Analysis/      # Market analysis and skill intelligence
│   ├── CareerIntel.Matching/      # AI-powered matching engine
│   ├── CareerIntel.Resume/        # Resume generation and ATS optimization
│   ├── CareerIntel.Intelligence/  # 20+ specialized career engines
│   ├── CareerIntel.Notifications/ # Telegram/Email notifications
│   ├── CareerIntel.Persistence/   # SQLite database and repositories
│   └── CareerIntel.Cli/           # Command-line interface
├── tests/
│   └── CareerIntel.Tests/         # Unit and integration tests
├── docs/
│   ├── MARKET_REPORT.md           # 2026 .NET market analysis
│   ├── SKILL_MATRIX.md            # Skill demand and salary impact
│   ├── CAREER_STRATEGY.md         # Optimization strategies
│   └── INTERVIEW_PREP.md          # Interview preparation guide
└── data/                          # Local data storage (SQLite + JSON)
```

### Key Technologies

- **.NET 10.0** - Modern C# with latest features
- **System.CommandLine** - Rich CLI experience
- **Entity Framework Core** - SQLite persistence
- **HtmlAgilityPack** - Web scraping
- **JSON serialization** - Data interchange

---

## ⚙️ Configuration

### Profile Setup

Create `data/my-profile.json` via CLI or manually:

```json
{
  "personal": {
    "name": "Your Name",
    "title": "Senior .NET Architect",
    "email": "you@example.com",
    "phone": "+1234567890",
    "location": "Kyiv, Ukraine",
    "linkedIn": "https://linkedin.com/in/yourprofile",
    "github": "https://github.com/yourusername"
  },
  "skills": [
    { "name": "C#", "level": "Expert", "yearsOfExperience": 10 },
    { "name": "ASP.NET Core", "level": "Expert", "yearsOfExperience": 8 },
    { "name": "Azure", "level": "Advanced", "yearsOfExperience": 6 }
  ],
  "experiences": [
    {
      "company": "Tech Corp",
      "title": "Lead .NET Architect",
      "startDate": "2020-01-01",
      "endDate": "2024-01-01",
      "achievements": ["Migrated monolith to microservices", "Led team of 8 engineers"]
    }
  ],
  "preferences": {
    "remote": true,
    "relocate": false,
    "minSalaryUsd": 120000,
    "targetSalaryUsd": 180000,
    "preferredLocations": ["Remote", "USA", "EU"],
    "excludedCompanies": []
  }
}
```

### Notification Setup

Create `data/notification-config.json`:

```json
{
  "telegram": {
    "enabled": true,
    "botToken": "YOUR_BOT_TOKEN",
    "chatId": "YOUR_CHAT_ID"
  },
  "email": {
    "enabled": false,
    "smtpHost": "smtp.gmail.com",
    "smtpPort": 587,
    "username": "you@gmail.com",
    "password": "your-app-password",
    "fromAddress": "you@gmail.com",
    "toAddress": "you@gmail.com"
  }
}
```

**Telegram Setup:**
1. Create bot via [@BotFather](https://t.me/BotFather) → get token
2. Send message to your bot
3. Get chat ID: `https://api.telegram.org/bot<TOKEN>/getUpdates`

---

## 📖 Command Reference

### Core Commands

| Command | Description |
|---------|-------------|
| `scan` | Scrape job vacancies from platforms |
| `analyze` | Analyze skill demand and market trends |
| `match` | Match and rank vacancies against profile |
| `dashboard` | Show unified overview of system state |
| `profile` | Create, view, and manage your profile |

### Career Intelligence

| Command | Description |
|---------|-------------|
| `assess` | AI-powered competitiveness assessment |
| `readiness` | Compute offer readiness and prep plan |
| `salary` | Salary intelligence across markets |
| `topics` | Infer interview topics from descriptions |
| `insights` | Aggregate interview feedback analysis |
| `companies` | Discover Ukraine-friendly companies |

### Application Tools

| Command | Description |
|---------|-------------|
| `resume` | Generate tailored resume and cover letter |
| `simulate` | Simulate ATS/recruiter/tech-lead review |
| `apply` | Auto-apply pipeline with tracking |
| `watch` | Monitor boards and auto-notify on matches |

### Learning & Preparation

| Command | Description |
|---------|-------------|
| `interview-prep` | Generate interview prep plans |
| `learn` | Learning plan with ROI-ranked skills |
| `learn-dynamic` | Dynamic learning system (scrape/study/quiz) |
| `resources` | Get personalized learning resources |
| `portfolio` | Generate portfolio project ideas |

### Advanced

| Command | Description |
|---------|-------------|
| `negotiate` | Analyze offer and generate strategy |
| `feedback` | Record interview feedback |
| `scan-image` | Extract vacancies from screenshots (OCR) |
| `run-all` | Run full pipeline (scan→analyze→match→readiness) |

### Options

```bash
--data-dir <path>    # Custom data directory (default: ./data)
--help               # Show command help
--version            # Show version
```

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

**Current Status:** 42 tests passing ✅

---

## 📊 Market Intelligence Reports

This project includes comprehensive market research:

- **[MARKET_REPORT.md](MARKET_REPORT.md)** - 2026 .NET market analysis
  - Compensation landscape ($60-250/hr)
  - Geographic arbitrage strategies
  - Platform-by-platform ROI analysis
  - Career path optimization (12-month roadmap)

- **[docs/SKILL_MATRIX.md](docs/SKILL_MATRIX.md)** - Skill demand matrix
  - Tier 1-4 skill classifications
  - Salary impact analysis
  - Co-occurrence patterns
  - Certification ROI

- **[docs/CAREER_STRATEGY.md](docs/CAREER_STRATEGY.md)** - Optimization strategies
- **[docs/INTERVIEW_PREP.md](docs/INTERVIEW_PREP.md)** - Interview preparation guide

---

## 🚧 Known Limitations

### Scraper Status (Last Tested: Feb 9, 2026)

**✅ Working Scrapers (13/21)** - 368 total vacancies tested ⬆️ 33% increase!
- **DouScraper** ✅ - 100 vacancies found (HTML parsing)
- **DjinniScraper** ✅ - 75 vacancies found (HTML parsing) **FIXED!**
- **NoFluffJobsScraper** ✅ - 65 vacancies found (HTML parsing)
- **RemotiveScraper** ✅ - 44 vacancies found (HTML parsing) **NEW!**
- **DiceScraper** ✅ - 38 vacancies found (HTML parsing) **NEW!**
- **WorkUaScraper** ✅ - 26 vacancies found (HTML parsing) **NEW!**
- **HimalayasScraper** ✅ - 6 vacancies found (JSON API)
- **HackerNewsScraper** ✅ - 5 vacancies found (Firebase API)
- **DynamiteJobsScraper** ✅ - 3 vacancies found (HTML parsing) **NEW!**
- **RemoteOkScraper** ✅ - 2 vacancies found (JSON API) **FIXED!**
- **BuiltInScraper** ✅ - 2 vacancies found (HTML parsing)
- **ArcDevScraper** ✅ - 1 vacancy found (HTML parsing)
- **JustRemoteScraper** ✅ - 1 vacancy found (HTML parsing) **NEW!**

**❌ Broken Scrapers (8/21)** - Require manual investigation
- **JustJoinItScraper** ❌ - All 4 API endpoints return 404 (API deprecated)
- **WeWorkRemotelyScraper** ❌ - Returns 406 Not Acceptable (public API removed)
- **ToptalScraper** ❌ - Returns 403 Forbidden (blocks automated requests)
- **WellfoundScraper** ❌ - Returns 403 Forbidden (blocks automated requests)
- **LinkedInScraper** ❌ - Blocked by robots.txt (requires authentication)
- **JobicyScraper** ❌ - API broken/changed (JSON parsing errors)
- **WorkingNomadsScraper** ❌ - Selectors not matching current HTML **NEW**
- **EuRemoteJobsScraper** ❌ - No unique vacancies found **NEW**

**Success Rate:** 62% (13/21 working) - **Improved from 60%!**

**Recent Additions (6 new scrapers):**
- **RemotiveScraper** ✅ - Popular remote job board, curated listings (44 vacancies)
- **DiceScraper** ✅ - Major IT job board worldwide (38 vacancies)
- **JustRemoteScraper** ✅ - Remote-focused listings (1 vacancy)
- **DynamiteJobsScraper** ✅ - Remote jobs board (3 vacancies)
- **WorkingNomadsScraper** ❌ - HTML structure changed, needs selector updates
- **EuRemoteJobsScraper** ❌ - Duplicate detection issue

**Data Quality:** The 13 working scrapers provide **368 vacancies** per scan, covering major .NET job markets (Ukraine, Poland, Europe, USA, Remote, Worldwide). This is excellent coverage for production use!

**Contributions Welcome:** The remaining 5 broken scrapers need manual website inspection to update API endpoints and HTML selectors. See [Contributing](#contributing) section.

### Other Limitations

- No authentication support for platforms requiring login
- OCR (scan-image) requires external Tesseract installation
- Notification system requires manual config file creation
- No web UI (CLI only)
- Some compiler warnings (CS9107) in primary constructors - safe to ignore

---

## 🛠️ Development

### Building from Source

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run tests
dotnet test

# Publish standalone executable
dotnet publish src/CareerIntel.Cli -c Release -r win-x64 --self-contained
```

### Project Structure

- **9 Projects** - Core, Scrapers, Analysis, Matching, Resume, Intelligence, Notifications, Persistence, CLI
- **149 Source Files** - Well-organized, documented code
- **Clean Architecture** - Domain-driven design with clear separation

### Scraper Status

**Latest Test (2026-02-09):** 15/21 scrapers working (71% success rate)

✅ **Working (188 vacancies from 1 page each):**
- Djinni (15), DOU (20), RemoteOK (2), HackerNews (1), Himalayas (1)
- Jobicy (5), NoFluffJobs (20), ArcDev (1), WorkUa (26), BuiltIn (2)
- Dice (37), Remotive (44), JustRemote (1), DynamiteJobs (3), EuRemoteJobs (10)

❌ **Broken (6 scrapers):**
- LinkedIn (robots.txt blocked)
- JustJoinIt (404 API)
- WeWorkRemotely (406 Not Acceptable)
- Toptal (403 Forbidden)
- Wellfound (403 Forbidden)
- WorkingNomads (HTML structure changed)

### Contributing

Contributions welcome! Areas needing help:
1. Scraper verification and maintenance
2. Additional job board integrations
3. Integration tests for scrapers
4. Web UI (Blazor?)
5. Docker containerization

---

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

Built with modern .NET technologies:
- [System.CommandLine](https://github.com/dotnet/command-line-api)
- [Entity Framework Core](https://docs.microsoft.com/ef/core/)
- [HtmlAgilityPack](https://html-agility-pack.net/)

Market data sourced from: Djinni, DOU, JustJoin.it, LinkedIn, and other platforms.

---

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/AlexOliinyk1/dotnet-career-intel/issues)
- **Documentation**: See `docs/` folder
- **Questions**: Open a discussion on GitHub

---

**Bottom line:** Stop trading time for a salary. Start trading expertise for rates. The .NET architect market is paying $120-250/hr for the right positioning. CareerIntel helps you get there with data, not guesswork.

---

⭐ **Star this repo** if it helps your career optimization journey!
