# Developer Overwatch — Design Spec

**Date:** 2026-05-26
**Status:** Draft
**Template Reference:** microsoft-agentic-harness (Clean Architecture patterns, governance engine, OpenTelemetry)

## Overview

Developer Overwatch is a session observability and governance system for teams using Claude Code to build software together. It captures every Claude Code session (conversations, tool calls, file changes), links sessions to the git commits and PRs they produced, and provides a dashboard where a head architect can review, annotate, and approve work.

### Goals

1. **Full session capture** — every conversation turn, tool call, and file change recorded
2. **Git traceability** — correlate sessions to commits and PRs via file-change matching
3. **Governance** — YAML policy engine flags violations (hardcoded secrets, raw SQL, missing auth, etc.)
4. **Architect review** — dashboard for reviewing PRs with linked session context
5. **Team analytics** — acceptance rates, violation trends, per-developer metrics

### Non-Goals (v1)

- Real-time session streaming (deferred to v2)
- Auto-blocking PRs based on governance violations (informational only in v1)
- Multi-tenant / SaaS deployment (single-team, self-hosted)

## Architecture

### Approach

Clean build using microsoft-agentic-harness as a pattern reference. Same Clean Architecture layers (Domain → Application → Infrastructure → Presentation), same tooling (MediatR CQRS, FluentValidation, OpenTelemetry), but fresh codebase purpose-built for session observability.

### System Components

```
DEVELOPER MACHINES                 CENTRAL SERVICE (Docker)             ARCHITECT
==================                 ========================             =========

Claude Code                        Developer Overwatch API              Dashboard SPA
  session-start hook --HTTP-->     POST /api/sessions/start             PR Queue
  post-tool-use hook --HTTP-->     POST /api/sessions/{id}/events       Session Review
  session-end hook   --HTTP-->     POST /api/sessions/{id}/end          Team Analytics
                                         |
Git Repo                                 v
  post-commit hook  --trailer-->   MediatR Pipeline
  .sessions/ (fallback)           Validate -> Govern -> Store
                                         |
GitHub                                   v
  push/PR webhooks  --HTTP-->      Database (SQLite v1 / Postgres v2)
                                         |
                                   Observability
                                   OpenTelemetry -> Jaeger + Prometheus
```

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Capture mechanism | Claude Code hooks | Zero-friction for developers. Hooks fire automatically on session-start, pre/post-tool-use, session-end. No CLI wrapper needed. |
| Log shipping | HTTP push primary, git fallback | HTTP POST to central API via local agent with SQLite queue. Falls back to committing `.sessions/{ID}.json` summaries when API is unreachable. |
| Session-commit linkage | File-change correlation | Match files touched in session artifacts against commit diffs. Confidence-scored. Optional git trailer as supplemental signal. No env var dependency. |
| Tech stack | .NET 10, Clean Architecture | Matches harness patterns. Single language across stack. User's preferred tooling. |
| Database | SQLite v1, Postgres v2 | SQLite for local Docker development. Postgres when multi-user concurrent access needed (Azure deployment). |
| Governance | YAML policy engine | Three rule types: pattern (regex), semantic (LLM-assisted), drift (EWMA). Adapted from harness Infrastructure.AI.Governance. |
| GitHub support | Cloud + Enterprise | Octokit with configurable base URL. Abstract via IGitHostingService interface. |
| Dashboard | React SPA + Tailwind | Served by Vite. Communicates with API over REST. Three views: PR Queue, Session Review, Team Analytics. React chosen for broader ecosystem of diff/code viewer components. |
| Deployment | Docker Compose local → Azure | Four containers: API, Dashboard, Jaeger, Prometheus. Azure deployment as v2 target. |
| Team size | Flexible | Designed to work for 2-5 developers on SQLite, scales to 15+ on Postgres. |

## Component Details

### 1. Capture Layer

#### Claude Code Hooks

Four hooks installed into each developer's `~/.claude/settings.json`:

**SessionStart hook:**
- Generate SESSION_ID (UUID), write to `~/.overwatch/active-session`
- Capture developer identity (git config user.email)
- Snapshot working directory state (tracked files + hashes)
- Write event to local queue (`~/.overwatch/queue.db`)

**PreToolUse hook:**
- Capture tool invocation intent before execution
- Log file state "before" for diff generation (hash target file if it exists)
- Write event to local queue

**PostToolUse hook:**
- Fires after every tool call (Bash, Edit, Write, Read, etc.)
- Captures: tool name, input params (truncated to 1KB), output summary (truncated to 2KB), duration
- For file writes: capture after-hash, compute diff against pre-tool snapshot
- Write event to local queue

**SessionEnd hook:**
- Final file state snapshot (compare to session start for net diff)
- Generate session summary: total turns, unique tools used, files touched, net lines changed
- Write event to local queue
- Signal local agent to flush queue

All hooks write to the local SQLite queue only — they never make HTTP calls directly. This keeps hook execution fast (<50ms) and prevents network latency from blocking the developer's Claude Code session.

#### Local Agent

A lightweight background service on each developer machine, distributed as a .NET global tool (`dotnet tool install -g overwatch-agent`). Cross-platform (Windows, macOS, Linux). Single-file publish for minimal footprint.

- **Event queue**: Hooks write events to a local SQLite queue (`~/.overwatch/queue.db`). Agent polls queue every 5 seconds, batches up to 50 events, POSTs to central API. Retries with exponential backoff (1s, 2s, 4s, 8s, max 60s) on failure. Events are idempotent (UUID-keyed, server deduplicates).
- **Conversation capture**: On session-end, reads Claude Code's conversation log if accessible (`~/.claude/projects/{project-hash}/conversations/`). If Claude's internal format changes or files aren't readable, conversation data is limited to what hooks captured (tool calls and file changes). This is a best-effort layer — the system works without it, just with less context.
- **Decision moment detection**: Heuristic-based with three signals: (1) user message immediately follows an assistant message containing a file write, and the user's next tool call modifies the same file (override), (2) user message contains redirect language ("no", "instead", "don't", "actually", "change this to"), (3) user manually edits a file that Claude just wrote (detected via pre/post-tool file hashes not matching session start snapshot). These are flagged as `is_decision = true` on the conversation turn.
- **Git fallback**: If queue exceeds threshold (100 events unshipped), generates compact JSON summary, writes to `.sessions/{SESSION_ID}.json` in repo. Post-commit hook adds trailer: `Claude-Session: {ID}`.
- **Privacy controls**: Configurable redaction patterns (secrets, tokens). Developer can mark sessions as private (excluded from review). File content diffs only — never raw file contents.

#### Installation

Single command: `dotnet overwatch install --api-url http://overwatch-server:5000`

Installs hooks into Claude Code settings, creates post-commit git hook, configures local agent, starts background service.

### 2. Central Service

ASP.NET Core API with MediatR CQRS pipeline.

#### Clean Architecture Layers

```
src/
├── Domain/
│   └── Domain.Core/                  # Entities, value objects, interfaces
│       ├── Entities/
│       │   ├── Session.cs
│       │   ├── ConversationTurn.cs
│       │   ├── ToolExecution.cs
│       │   ├── FileChange.cs
│       │   ├── DecisionMoment.cs
│       │   ├── GovernanceViolation.cs
│       │   ├── PullRequestReview.cs
│       │   └── CommitSessionLink.cs
│       ├── Enums/
│       │   ├── SessionStatus.cs      # Active, Completed, Failed
│       │   ├── ReviewStatus.cs       # Pending, Approved, NeedsChanges
│       │   └── ViolationSeverity.cs  # Info, Warning, Error, Critical
│       └── Interfaces/
│           ├── ISessionRepository.cs
│           ├── IGovernanceEngine.cs
│           ├── IGitHostingService.cs
│           └── ISessionCorrelator.cs
│
├── Application/
│   └── Application.Core/
│       ├── Commands/
│       │   ├── IngestSessionStart/
│       │   ├── IngestSessionEvents/
│       │   ├── IngestSessionEnd/
│       │   ├── ProcessWebhook/
│       │   ├── SubmitReview/
│       │   └── AddAnnotation/
│       ├── Queries/
│       │   ├── GetSession/
│       │   ├── GetPRQueue/
│       │   ├── GetTeamAnalytics/
│       │   └── GetViolations/
│       └── Behaviors/
│           ├── ValidationBehavior.cs
│           ├── GovernanceBehavior.cs
│           └── LoggingBehavior.cs
│
├── Infrastructure/
│   ├── Infrastructure.Persistence/
│   │   ├── OverwatchDbContext.cs
│   │   ├── Migrations/
│   │   └── Repositories/
│   ├── Infrastructure.Governance/
│   │   ├── GovernanceEngine.cs
│   │   ├── PolicyLoader.cs
│   │   └── Rules/
│   │       ├── PatternRule.cs
│   │       ├── DriftDetector.cs
│   │       └── SemanticRule.cs
│   ├── Infrastructure.GitHub/
│   │   ├── GitHubService.cs
│   │   └── WebhookParser.cs
│   ├── Infrastructure.Observability/
│   └── Infrastructure.Correlation/
│
└── Presentation/
    ├── Presentation.API/
    │   ├── Controllers/
    │   │   ├── SessionsController.cs
    │   │   ├── WebhooksController.cs
    │   │   ├── ReviewsController.cs
    │   │   └── AnalyticsController.cs
    │   └── Middleware/
    │       ├── ApiKeyAuthMiddleware.cs
    │       └── WebhookSignatureMiddleware.cs
    └── Presentation.Dashboard/
```

#### API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/sessions/start` | Begin new session |
| POST | `/api/sessions/{id}/events` | Batch ingest tool/conversation events |
| POST | `/api/sessions/{id}/end` | Finalize session, trigger governance |
| GET | `/api/sessions/{id}` | Full session with turns, tools, diffs |
| GET | `/api/sessions?developer=&repo=` | Filter sessions |
| POST | `/api/webhooks/github` | GitHub push/PR webhook receiver |
| GET | `/api/prs` | PR queue with linked sessions |
| GET | `/api/prs/{repo}/{number}` | Single PR with all linked sessions |
| POST | `/api/prs/{repo}/{number}/review` | Submit architect review |
| POST | `/api/annotations` | Add architect annotation |
| GET | `/api/analytics/team` | Team-level metrics |
| GET | `/api/analytics/developer/{email}` | Per-developer metrics |
| GET | `/api/violations?session=&pr=` | Governance violations |

#### Authentication

- Developer agents authenticate via API key (generated during `overwatch install`)
- Architect dashboard authenticated via configurable provider (local credentials for v1, OAuth/OIDC for v2)
- GitHub webhooks validated via HMAC signature verification

### 3. Database Schema

```sql
-- Core session data
CREATE TABLE sessions (
    id              TEXT PRIMARY KEY,  -- UUID
    developer_email TEXT NOT NULL,
    developer_name  TEXT,
    repo_path       TEXT,
    status          TEXT NOT NULL,     -- Active/Completed/Failed
    started_at      TEXT NOT NULL,     -- ISO 8601
    ended_at        TEXT,
    summary_json    TEXT,
    created_at      TEXT DEFAULT (datetime('now'))
);

CREATE TABLE conversation_turns (
    id              TEXT PRIMARY KEY,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    turn_index      INTEGER NOT NULL,
    role            TEXT NOT NULL,     -- user/assistant/system
    content         TEXT,
    token_count     INTEGER,
    timestamp       TEXT NOT NULL,
    is_decision     INTEGER DEFAULT 0
);

CREATE TABLE tool_executions (
    id              TEXT PRIMARY KEY,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    turn_id         TEXT REFERENCES conversation_turns(id),
    tool_name       TEXT NOT NULL,
    input_params    TEXT,             -- JSON
    output_summary  TEXT,
    duration_ms     INTEGER,
    timestamp       TEXT NOT NULL
);

CREATE TABLE file_changes (
    id              TEXT PRIMARY KEY,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    tool_exec_id    TEXT REFERENCES tool_executions(id),
    file_path       TEXT NOT NULL,
    before_hash     TEXT,
    after_hash      TEXT,
    diff_text       TEXT,
    change_type     TEXT              -- Created/Modified/Deleted
);

-- Git correlation
CREATE TABLE commit_session_links (
    id              TEXT PRIMARY KEY,
    commit_sha      TEXT NOT NULL,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    repo            TEXT NOT NULL,
    confidence      REAL,
    link_method     TEXT              -- file_match/trailer/manual
);

-- Governance
CREATE TABLE governance_violations (
    id              TEXT PRIMARY KEY,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    rule_id         TEXT NOT NULL,
    severity        TEXT NOT NULL,    -- Info/Warning/Error/Critical
    message         TEXT NOT NULL,
    context_json    TEXT,
    resolved        INTEGER DEFAULT 0,
    resolved_by     TEXT,
    resolved_at     TEXT
);

-- Architect review
CREATE TABLE pull_request_reviews (
    id              TEXT PRIMARY KEY,
    pr_number       INTEGER NOT NULL,
    repo            TEXT NOT NULL,
    status          TEXT NOT NULL,    -- Pending/Approved/NeedsChanges
    reviewed_by     TEXT,
    reviewed_at     TEXT,
    notes           TEXT
);

CREATE TABLE architect_annotations (
    id              TEXT PRIMARY KEY,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    turn_id         TEXT REFERENCES conversation_turns(id),
    tool_exec_id    TEXT REFERENCES tool_executions(id),
    content         TEXT NOT NULL,
    created_by      TEXT NOT NULL,
    created_at      TEXT DEFAULT (datetime('now'))
);

-- Indexes
CREATE INDEX idx_sessions_developer ON sessions(developer_email);
CREATE INDEX idx_sessions_status ON sessions(status);
CREATE INDEX idx_turns_session ON conversation_turns(session_id);
CREATE INDEX idx_tools_session ON tool_executions(session_id);
CREATE INDEX idx_files_session ON file_changes(session_id);
CREATE INDEX idx_commits_session ON commit_session_links(session_id);
CREATE INDEX idx_commits_sha ON commit_session_links(commit_sha);
CREATE INDEX idx_violations_session ON governance_violations(session_id);
CREATE INDEX idx_violations_severity ON governance_violations(severity);
CREATE INDEX idx_pr_reviews_repo ON pull_request_reviews(repo, pr_number);
```

### 4. Governance Engine

Adapted from harness `Infrastructure.AI.Governance`.

#### Policy Format

Policies defined in YAML files under `policies/` directory:

```yaml
policies:
  - id: no-raw-sql
    name: "No raw SQL outside repositories"
    severity: error
    type: pattern
    scan: [diffs, conversation]
    patterns:
      - "SqlCommand|SqlConnection"
      - "ExecuteSqlRaw|FromSqlRaw"
    exclude_paths:
      - "**/Repositories/**"
      - "**/Migrations/**"

  - id: no-hardcoded-secrets
    name: "No hardcoded secrets"
    severity: critical
    type: pattern
    scan: [diffs]
    patterns:
      - "password\\s*=\\s*[\"'][^\"']+[\"']"
      - "api[_-]?key\\s*=\\s*[\"'][^\"']+[\"']"
      - "connectionString.*=.*[\"']Server="

  - id: auth-middleware-required
    name: "API endpoints must have auth"
    severity: error
    type: semantic
    scan: [diffs]
    rule: >
      Any new controller action or minimal API endpoint
      must have [Authorize] or RequireAuthorization()

  - id: test-coverage-required
    name: "New public methods need tests"
    severity: info
    type: semantic
    scan: [session]
    rule: >
      If session created new public methods, check if
      corresponding test files were also created or modified
```

#### Rule Types

1. **Pattern** — regex matching against diffs and conversation content. Fast, deterministic, zero external dependencies.
2. **Semantic** — LLM-assisted analysis for rules that can't be expressed as regex. Uses Azure OpenAI or Claude endpoint. Runs asynchronously, results may lag.
3. **Drift** — EWMA (Exponentially Weighted Moving Average) statistical detection adapted from harness. Flags when a developer's interaction patterns deviate significantly: unusual override rate, high tool failure rate, sudden scope changes. Runs on a rolling window of the last 20 sessions.

#### Execution Pipeline

1. Triggered via MediatR `GovernanceBehavior` on session end
2. Pattern rules run first (fast, synchronous)
3. Semantic rules queued for async processing
4. Drift detection runs on rolling window
5. All violations written to `governance_violations` table
6. Dashboard displays violations inline with session context

#### Architect Controls

- Resolve violations with reason (false positive, accepted risk)
- Add/edit/disable policies via dashboard or YAML file
- Severity thresholds: block PR approval above configured level
- Per-developer or per-repo policy overrides

### 5. Dashboard

React SPA with Tailwind CSS, served by Vite.

#### View 1 — PR Queue

- List of open PRs with status badges: Pending / Approved / Needs Changes
- Summary counters at top (pending, approved, needs changes)
- Table columns: PR title, developer, files changed, linked sessions count, violation count, status
- Click a PR to enter Session Review
- Sortable and filterable by status, developer, date, violation severity

#### View 2 — Session Review

Split-pane layout:

- **Left panel**: PR diff (standard file diff view with syntax highlighting)
  - Click a diff line to see which session/turn produced it
  - Governance violations shown inline with the code they flag
- **Right panel**: Claude session timeline
  - Conversation turns rendered as chat messages
  - Tool calls shown inline as collapsible blocks (tool name, params, output, duration)
  - File changes highlighted and cross-linked to the diff on the left
  - Decision moments highlighted (turns where developer redirected Claude)
  - Architect can add annotations on any turn or tool call
- **Action bar**: Approve / Request Changes buttons that post back to GitHub PR API

#### View 3 — Team Analytics

- Summary cards: sessions this week, approval rate, Claude acceptance rate, violations this week
- Per-developer table: sessions, commits, acceptance rate, violation count
- Top governance violations chart
- Trend lines for key metrics (weekly rolling)

### 6. GitHub Integration

#### Webhook Listener

- Receives `push` and `pull_request` events
- On push: parses commit messages for `Claude-Session:` trailers, creates `commit_session_links` records
- On PR open/update: triggers session correlation for all commits in the PR, aggregates linked sessions, updates PR status in dashboard
- Signature verification via `WebhookSignatureMiddleware` (HMAC-SHA256)

#### Session-Commit Correlation

Primary linkage method — file-change correlation:

1. For each commit in a PR, extract the list of changed files
2. Query `file_changes` table for sessions that touched those same files
3. Score by overlap: `confidence = matched_files / total_files_in_commit`
4. Filter by time window: session must have ended within a configurable window before the commit (default: 2 hours)
5. Store as `commit_session_links` with `link_method = 'file_match'`

Secondary: git trailer parsing (`Claude-Session: {ID}`) stored with `link_method = 'trailer'`.

#### GitHub App Configuration

- Configurable base URL (github.com or Enterprise Server)
- `IGitHostingService` interface abstracts provider-specific API differences
- Permissions: read commits, read/write PRs, read/write checks
- Webhook events: `push`, `pull_request`

## Deployment

### v1 — Local Docker Compose

Four services:

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| overwatch-api | Custom (.NET 10) | 5000 | Central API |
| dashboard | Custom (Vite) | 3000 | Architect SPA |
| jaeger | jaegertracing/all-in-one | 16686, 4317 | Trace visualization |
| prometheus | prom/prometheus | 9090 | Metrics scraping |

SQLite database stored in a Docker volume. Policies mounted from host.

### v2 — Azure (future)

- API → Azure Container Apps
- Database → Azure Database for PostgreSQL
- Dashboard → Azure Static Web Apps
- Observability → Azure Monitor / Application Insights
- Auth → Entra ID (Azure AD)

## Build Order

Six phases as vertical slices:

1. **Foundation** — Solution structure, domain entities, database schema, MediatR pipeline, Docker Compose
2. **Capture & Ingest** — Session ingestion API, Claude Code hooks, local agent with queue, E2E test
3. **GitHub Integration** — Webhook listener, session-commit correlation, git hooks, PR status API
4. **Governance Engine** — YAML policy loader, pattern rules, semantic rules, drift detector, pipeline behavior
5. **Dashboard** — SPA scaffolding, PR Queue, Session Review, Team Analytics, annotations, review actions
6. **Observability & Polish** — OpenTelemetry spans, Prometheus metrics, installer CLI, README

## Constraints

- All components run locally via Docker Compose
- Git hooks are opt-in (installed via `dotnet overwatch install`)
- No vendor lock-in — fully self-hosted
- Developer privacy: configurable redaction, private session marking, diffs only (no raw file content)
- 80% test coverage on new code
