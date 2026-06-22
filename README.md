# Developer Overwatch

Monitors Claude Code sessions in real time, enforces governance policies, and surfaces analytics through a React dashboard.

**Workspace:** `agentic-architecture-and-governance/accelerators/developer-overwatch/` — usage telemetry **accelerator**. Architecture: [`../../architecture/agentic-architecture/`](../../architecture/agentic-architecture/). Governance: [`../../governance/ai-governance-in-practice/`](../../governance/ai-governance-in-practice/).

[![CI](https://github.com/MCKRUZ/developer-overwatch/actions/workflows/ci.yml/badge.svg)](https://github.com/MCKRUZ/developer-overwatch/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![React 19](https://img.shields.io/badge/React-19.1-61DAFB)
![License](https://img.shields.io/badge/license-MIT-blue)

## Overview

Developer Overwatch captures telemetry from Claude Code sessions -- conversation turns, tool executions, file changes, and architectural decisions -- via lightweight hooks that write to a local SQLite queue. A .NET global tool (the Agent) ships batched events to a central API server for storage and analysis.

The API server runs a governance engine on session completion. Pattern-based rules catch violations via regex (secrets in code, deep nesting, oversized files), while semantic rules use an LLM to evaluate higher-level concerns (architecture drift, missing tests, scope creep). A drift detector tracks violation frequency over sliding windows and escalates when thresholds are exceeded.

A React dashboard provides live session monitoring via SignalR, filterable violation views, team and per-developer analytics with charts, PR review tracking linked to originating sessions, and PDF/CSV export. Authentication supports both API key (for agents) and OIDC/JWT (for the dashboard).

## Architecture

```
+-------------------+     hooks     +-------------------+     HTTP      +-------------------+
|   Claude Code     | ------------> |   Agent (.NET     | ----------->  |   API Server      |
|   Session         |  SessionStart |   Global Tool)    |  POST /api/*  |   (ASP.NET Core)  |
|                   |  PostToolUse  |                   |               |                   |
|                   |  Stop         | - SQLite queue    |               | - MediatR CQRS    |
+-------------------+               | - Privacy redact  |               | - EF Core + SQLite|
                                    | - Git fallback    |               | - Governance Eng. |
                                    +-------------------+               | - SignalR hub     |
                                                                        +--------+----------+
                                                                                 |
                                                                          SignalR | REST
                                                                                 |
                                                                        +--------v----------+
                                                                        |   Dashboard       |
                                                                        |   (React + Vite)  |
                                                                        |                   |
                                                                        | - Recharts        |
                                                                        | - Tailwind CSS    |
                                                                        | - OIDC auth       |
                                                                        +-------------------+
```

### Projects

| Project | Layer | Purpose |
|---------|-------|---------|
| `DeveloperOverwatch.Domain` | Domain | Entities (`Session`, `ConversationTurn`, `ToolExecution`, `FileChange`, `GovernanceViolation`, `CommitSessionLink`, `PullRequestReview`, `ArchitectAnnotation`), enums, `Result<T>`, repository/service interfaces |
| `DeveloperOverwatch.Application` | Application | MediatR commands/queries (CQRS), FluentValidation, pipeline behaviors (request tracing, idempotency, validation, logging, governance), session-commit correlation |
| `DeveloperOverwatch.Infrastructure` | Infrastructure | EF Core persistence (SQLite/SQL Server), repository implementations, GitHub service, governance engine (pattern rules, semantic rules, drift detection, LLM client), Slack/email notifications |
| `DeveloperOverwatch.API` | Presentation | ASP.NET Core controllers, SignalR hub (`/hubs/overwatch`), middleware (global exception handling, security headers, request logging, webhook signature verification), dual auth (API key + JWT), rate limiting, Swagger/OpenAPI |
| `DeveloperOverwatch.Agent` | Agent | .NET global tool (`overwatch-agent`), local SQLite queue, privacy redaction, decision detection, conversation capture, git fallback writer, background queue processor |
| `dashboard` | Frontend | React 19 + Vite + Tailwind CSS + Recharts, OIDC authentication, SignalR real-time updates, session timeline, governance violations, analytics charts, PDF/CSV export |

## Features

- **Session Tracking** -- Captures conversation turns, tool executions, and file changes from Claude Code sessions
- **Governance Engine** -- Pattern-based rules (regex) and semantic rules (LLM-powered) evaluate sessions on completion
- **Drift Detection** -- EWMA-based sliding window tracks violation frequency and escalates when thresholds are breached
- **Real-Time Dashboard** -- SignalR pushes session starts, events, endings, and violation resolutions to connected clients
- **Analytics** -- Team-level and per-developer metrics with Recharts visualizations
- **PR Review Tracking** -- Links pull requests to originating Claude Code sessions via commit correlation
- **Architect Annotations** -- Attach notes to sessions, specific turns, or tool executions
- **Dual Authentication** -- API key auth for agents, OIDC/JWT for dashboard users
- **Privacy Redaction** -- Agent strips sensitive content before shipping events
- **Git Fallback** -- Agent writes events to git notes when the API is unreachable
- **Export** -- PDF and CSV export of session data and analytics
- **Dark Mode** -- Tailwind-based dark theme support
- **Notifications** -- Slack webhooks and email alerts for governance violations
- **Webhook Integration** -- GitHub push and pull_request webhooks with HMAC signature verification
- **Idempotent Webhooks** -- Duplicate deliveries are deduplicated by `X-GitHub-Delivery` id, so provider retries never double-process
- **Structured Error Responses** -- RFC 7807 ProblemDetails on all failures (detailed in development, generic in production)
- **Distributed Tracing** -- Each request is wrapped in an OpenTelemetry span (exported to Application Insights when configured)
- **Rate Limiting** -- Fixed-window rate limiting per IP, separate policy for webhooks
- **Health Checks** -- `/health` endpoint with database connectivity checks

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/) and npm
- (Optional) An OIDC provider for dashboard authentication (Entra ID, Auth0, etc.)

## Getting Started

```bash
# Clone the repository
git clone https://github.com/MCKRUZ/developer-overwatch.git
cd developer-overwatch

# Build the backend
dotnet build

# Run the API server (defaults to http://localhost:5000)
dotnet run --project src/API/DeveloperOverwatch.API

# In a separate terminal, install frontend dependencies and start the dev server
cd dashboard
npm install
npm run dev
```

- Dashboard: http://localhost:5173 (Vite proxies `/api` and `/hubs` to the API server)
- Swagger UI: http://localhost:5000/swagger (development mode only)
- Health check: http://localhost:5000/health

The API server auto-creates a SQLite database (`overwatch.db`) on first startup. No manual migration step is needed for SQLite.

## Local Agent Setup

The agent is a .NET global tool that runs as a background service, shipping Claude Code hook events to the API server.

```powershell
# Pack the agent
dotnet pack src/Agent/DeveloperOverwatch.Agent -c Release -o ./nupkg

# Install globally
dotnet tool install --global --add-source ./nupkg DeveloperOverwatch.Agent

# Install hooks into Claude Code settings and create agent config
.\hooks\install-hooks.ps1 -ApiUrl http://localhost:5000 -ApiKey your-api-key

# Start the agent (runs as a background service)
overwatch-agent
```

The install script:
1. Creates `~/.overwatch/config.json` with API URL, API key, poll interval, and batch size
2. Registers three hooks in `~/.claude/settings.json`: `SessionStart`, `PostToolUse`, and `Stop`
3. Hooks write events to a local SQLite queue at `~/.overwatch/queue.db`

The agent polls the queue and ships events in batches to the API. If the API is unreachable, events accumulate locally. When the queue exceeds the git fallback threshold, events are also written to git notes as a safety net.

## Configuration

### API Server (`appsettings.json`)

| Section | Key | Default | Description |
|---------|-----|---------|-------------|
| `Database` | `Provider` | `sqlite` | Database provider (`sqlite` or `sqlserver`) |
| `Database` | `ConnectionString` | `Data Source=/data/overwatch.db` | Database connection string |
| `ApiKeys` | `Keys` | `[]` | Array of valid API keys for agent authentication |
| `Authentication` | `Authority` | `""` | OIDC authority URL (leave empty to disable JWT auth) |
| `Authentication` | `Audience` | `""` | OIDC audience / resource identifier |
| `Authentication` | `RequireHttpsMetadata` | `true` | Require HTTPS for OIDC metadata endpoint |
| `Cors` | `Origins` | `["http://localhost:3000", "http://localhost:5173"]` | Allowed CORS origins |
| `GitHub` | `BaseUrl` | `https://api.github.com` | GitHub API base URL |
| `GitHub` | `Token` | `""` | GitHub personal access token for PR operations |
| `GitHub` | `WebhookSecret` | `""` | HMAC secret for webhook signature verification |
| `RateLimiting` | `Default:PermitLimit` | `100` | Requests per window (default policy) |
| `RateLimiting` | `Default:WindowSeconds` | `60` | Window duration in seconds (default policy) |
| `RateLimiting` | `Webhook:PermitLimit` | `30` | Requests per window (webhook policy) |
| `RateLimiting` | `Webhook:WindowSeconds` | `60` | Window duration in seconds (webhook policy) |
| `Notifications` | `Slack:Enabled` | `false` | Enable Slack webhook notifications |
| `Notifications` | `Slack:WebhookUrl` | `""` | Slack incoming webhook URL |
| `Notifications` | `Slack:MinimumSeverity` | `Error` | Minimum violation severity to notify |
| `Notifications` | `Email:Enabled` | `false` | Enable email notifications |
| `Notifications` | `Email:SmtpHost` | `""` | SMTP server hostname |
| `Notifications` | `Email:SmtpPort` | `587` | SMTP server port |
| `Notifications` | `Email:UseSsl` | `true` | Use SSL/TLS for SMTP |
| `Notifications` | `Email:FromAddress` | `""` | Sender email address |
| `Notifications` | `Email:ToAddresses` | `[]` | Recipient email addresses |
| `Notifications` | `Email:MinimumSeverity` | `Critical` | Minimum violation severity to email |

### Governance Engine (`appsettings.json` under `Governance`)

| Key | Default | Description |
|-----|---------|-------------|
| `PoliciesPath` | `policies` | Path to YAML policy definition files |
| `DriftWindowHours` | `72` | Sliding window for drift detection |
| `DriftThreshold` | `3` | Violation count to trigger drift escalation |
| `DriftAlpha` | `0.3` | EWMA smoothing factor |
| `DriftBucketHours` | `4` | Time bucket size for drift aggregation |
| `LlmApiUrl` | `null` | API URL for semantic rule evaluation (Claude API) |
| `LlmApiKey` | `null` | API key for LLM calls |
| `LlmModel` | `claude-sonnet-4-6` | Model for semantic rule evaluation |
| `LlmMaxTokens` | `1024` | Max tokens per LLM response |
| `LlmTimeoutSeconds` | `30` | LLM request timeout |
| `SemanticJurySize` | `1` | Number of independent judges per semantic rule. `1` is the single-judge default; `>1` runs a panel and reduces findings by consensus |
| `SemanticJuryPersonas` | `null` | Optional list of distinct judge "lenses"; when set, drives panel size and overrides `SemanticJurySize` (empty/null ⇒ identical judges) |
| `SemanticConsensusThreshold` | `0.5` | Fraction of judges that must agree for a panel finding to stand (`0.5` = simple majority) |

### Dashboard Environment Variables

Create `dashboard/.env.local` with the following variables (all optional — omit them to run the dashboard without authentication):

| Variable | Description |
|----------|-------------|
| `VITE_OIDC_AUTHORITY` | OIDC provider URL (e.g., `https://login.microsoftonline.com/{tenant}/v2.0`) |
| `VITE_OIDC_CLIENT_ID` | OIDC client/application ID |
| `VITE_OIDC_REDIRECT_URI` | OAuth redirect URI (defaults to `window.location.origin`) |
| `VITE_OIDC_SCOPE` | OAuth scopes (defaults to `openid profile email`) |

When OIDC variables are not set, the dashboard runs without authentication.

### Agent Configuration (`~/.overwatch/config.json`)

| Key | Default | Description |
|-----|---------|-------------|
| `apiUrl` | `http://localhost:5000` | API server URL |
| `apiKey` | `""` | API key for authentication |
| `pollIntervalSeconds` | `5` | Queue polling interval |
| `batchSize` | `50` | Events per batch |
| `gitFallbackThreshold` | `100` | Queue depth before writing git fallback |

## Testing

### Backend

```bash
# Run all backend tests
dotnet test

# Run a specific test project
dotnet test tests/DeveloperOverwatch.Application.Tests
```

**Test projects and coverage areas:**

| Project | Tests |
|---------|-------|
| `DeveloperOverwatch.Domain.Tests` | `Result<T>` behavior |
| `DeveloperOverwatch.Application.Tests` | Commands (ingest, webhooks, review, resolve, annotate), queries (violations, analytics), behaviors (validation, governance), services (correlation, notifications), governance engine (policy loader, pattern evaluator, semantic evaluator, drift detector, LLM client) |
| `DeveloperOverwatch.API.Tests` | Controller integration tests (sessions, analytics, governance, annotations, PRs, webhooks), webhook signature middleware |
| `DeveloperOverwatch.Agent.Tests` | Event queue, privacy redactor, decision detector, API client, queue processor, git fallback writer |

### Frontend

```bash
cd dashboard

# Run all frontend tests
npx vitest run

# Run with coverage
npx vitest run --coverage

# Watch mode
npx vitest
```

Frontend tests cover: `Layout`, `StatusBadge`, `SeverityBadge`, `ChartCard`, `SessionTimeline`, `ExportButtons`, and all pages (`SessionsPage`, `SessionDetailPage`, `PrQueuePage`, `GovernancePage`, `AnalyticsPage`).

## Deployment

### Azure Container Apps via Bicep

The `infra/` directory contains modular Bicep templates for deploying to Azure:

- `main.bicep` -- Orchestrator (container registry, monitoring, key vault, optional SQL, container apps)
- `modules/container-registry.bicep` -- Azure Container Registry
- `modules/monitoring.bicep` -- Log Analytics + Application Insights
- `modules/key-vault.bicep` -- Azure Key Vault
- `modules/sql-database.bicep` -- Azure SQL (optional, defaults to SQLite with Azure Files)
- `modules/container-apps.bicep` -- Container Apps for API and Dashboard

```bash
# Deploy infrastructure
az deployment group create \
  --resource-group overwatch-rg \
  --template-file infra/main.bicep \
  --parameters infra/parameters/production.bicepparam
```

Environment-specific parameter files: `infra/parameters/dev.bicepparam` and `infra/parameters/production.bicepparam`.

### CI/CD (GitHub Actions)

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | PRs + pushes to main/master | Backend build + test with coverage gate, dashboard typecheck + test, supply-chain vulnerability scan, pack agent tool |
| `deploy.yml` | Push to main, manual dispatch | Build Docker images, push to ACR, deploy Bicep, update Container Apps |
| `agent-release.yml` | `agent-v*` tags | Pack and publish agent to NuGet.org |

**Required secrets for deployment:**

| Secret | Description |
|--------|-------------|
| `AZURE_CREDENTIALS` | Service principal JSON |
| `AZURE_SUBSCRIPTION_ID` | Target Azure subscription |
| `AZURE_RESOURCE_GROUP` | Target resource group |
| `ACR_LOGIN_SERVER` | ACR hostname |
| `NUGET_API_KEY` | NuGet.org API key (agent release only) |

## API Endpoints

### Sessions

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/sessions/start` | API Key | Start a new session |
| `POST` | `/api/sessions/{sessionId}/events` | API Key | Ingest conversation turns, tool executions, and file changes |
| `POST` | `/api/sessions/{sessionId}/end` | API Key | End a session (triggers governance evaluation) |
| `GET` | `/api/sessions` | Any | List sessions (filter by `?developer=` and `?repo=`) |
| `GET` | `/api/sessions/{sessionId}` | Any | Get session detail with all events |

### Pull Requests

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/api/prs` | Any | List pull request reviews (filter by `?repo=`) |
| `GET` | `/api/prs/{owner}/{repo}/{prNumber}` | Any | Get PR review detail |
| `POST` | `/api/prs/{owner}/{repo}/{prNumber}/review` | Any | Create/update a PR review |

### Webhooks

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/webhooks/github` | Anonymous (HMAC) | Handle GitHub `push` and `pull_request` events |

### Governance

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/api/governance/violations/{sessionId}` | Any | Get violations for a session (filter by `?severity=`) |
| `POST` | `/api/governance/violations/{violationId}/resolve` | Any | Resolve a violation with reason |

### Analytics

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/api/analytics/team` | Any | Team-level analytics and metrics |
| `GET` | `/api/analytics/developer/{email}` | Any | Per-developer analytics |

### Annotations

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/annotations` | Any | Add an annotation to a session, turn, or tool execution |

### Auth

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/api/auth/me` | Any | Get current authenticated user info |

### System

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/health` | Anonymous | Health check with database connectivity |
| `WS` | `/hubs/overwatch` | Anonymous | SignalR hub for real-time session events |

## Project Structure

```
developer-overwatch/
+-- src/
|   +-- Domain/DeveloperOverwatch.Domain/
|   |   +-- Common/              # Result<T>
|   |   +-- Entities/            # Session, ConversationTurn, ToolExecution, FileChange,
|   |   |                        # GovernanceViolation, CommitSessionLink, PullRequestReview,
|   |   |                        # ArchitectAnnotation
|   |   +-- Enums/               # SessionStatus, ViolationSeverity, PolicyType, etc.
|   |   +-- Interfaces/          # Repository and service contracts
|   |   +-- Models/              # GovernancePolicy
|   +-- Application/DeveloperOverwatch.Application/
|   |   +-- Behaviors/           # RequestTracingBehavior, IdempotencyBehavior, ValidationBehavior,
|   |   |                        # LoggingBehavior, GovernanceBehavior
|   |   +-- Idempotency/          # IIdempotentRequest, IIdempotencyStore, InMemoryIdempotencyStore
|   |   +-- Commands/            # IngestSessionStart/Events/End, ProcessPushWebhook,
|   |   |                        # ProcessPrWebhook, ReviewPullRequest, ResolveViolation,
|   |   |                        # AddAnnotation
|   |   +-- Queries/             # GetSession(s), GetPullRequest(s), GetViolations,
|   |   |                        # GetTeamAnalytics, GetDeveloperAnalytics
|   |   +-- Services/            # SessionCommitCorrelator
|   +-- Infrastructure/DeveloperOverwatch.Infrastructure/
|   |   +-- Governance/          # GovernanceEngine, PatternRuleEvaluator, SemanticRuleEvaluator,
|   |   |                        # DriftDetector, PolicyLoader, LlmClient
|   |   +-- Persistence/         # OverwatchDbContext, repositories
|   |   +-- Services/            # GitHubService, SlackNotificationService, EmailNotificationService,
|   |                            # CompositeNotificationService
|   +-- API/DeveloperOverwatch.API/
|   |   +-- Controllers/         # Sessions, PullRequests, Webhooks, Governance, Analytics,
|   |   |                        # Annotations, Auth
|   |   +-- Hubs/                # OverwatchHub (SignalR)
|   |   +-- Middleware/          # GlobalException, SecurityHeaders, RequestLogging, WebhookSignature
|   |   +-- Authentication/      # ApiKeyAuthenticationHandler
|   +-- Agent/DeveloperOverwatch.Agent/
|       +-- Models/              # AgentConfig, EventType, QueuedEvent
|       +-- Services/            # EventQueue, PrivacyRedactor, DecisionDetector,
|                                # ConversationCapture, GitFallbackWriter, ApiClient,
|                                # QueueProcessor
+-- tests/
|   +-- DeveloperOverwatch.Domain.Tests/
|   +-- DeveloperOverwatch.Application.Tests/
|   +-- DeveloperOverwatch.API.Tests/
|   +-- DeveloperOverwatch.Agent.Tests/
+-- dashboard/
|   +-- src/
|   |   +-- auth/                # AuthProvider, LoginPage, ProtectedRoute
|   |   +-- components/          # Layout, ChartCard, SessionTimeline, TimelineEvent,
|   |   |                        # StatusBadge, SeverityBadge, ExportButtons
|   |   +-- pages/               # SessionsPage, SessionDetailPage, PrQueuePage,
|   |                            # GovernancePage, AnalyticsPage
|   +-- vite.config.ts           # Dev server with API/SignalR proxy
+-- hooks/
|   +-- install-hooks.ps1        # Hook installer for Claude Code
|   +-- overwatch-session-start.ps1
|   +-- overwatch-post-tool-use.ps1
|   +-- overwatch-session-end.ps1
+-- infra/
|   +-- main.bicep               # Azure deployment orchestrator
|   +-- modules/                 # container-registry, monitoring, key-vault, sql-database, container-apps
|   +-- parameters/              # dev.bicepparam, production.bicepparam
+-- .github/workflows/
    +-- ci.yml                   # Build + test
    +-- deploy.yml               # Docker build + Azure deploy
    +-- agent-release.yml        # NuGet publish
```

## License

MIT
