# Dashboard Frontend Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a comprehensive test suite to the React dashboard achieving 80%+ coverage across lines, branches, and functions.

**Architecture:** Bottom-up testing — infrastructure first, then API client unit tests, then component tests, then page integration tests. Each layer validates before the next builds on it. Module-level mocks (`vi.mock`) for the API client in page tests; `globalThis.fetch` mocks for API client unit tests.

**Tech Stack:** Vitest 3.x, @vitest/coverage-v8, @testing-library/react, @testing-library/jest-dom, @testing-library/user-event, jsdom

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `dashboard/vitest.config.ts` | Test runner config extending vite.config.ts |
| Create | `dashboard/src/test/setup.ts` | Jest-dom matchers import |
| Create | `dashboard/src/test/render.tsx` | Custom render with MemoryRouter |
| Create | `dashboard/src/test/factories.ts` | Type-safe mock data builders |
| Create | `dashboard/src/api.test.ts` | API client unit tests |
| Create | `dashboard/src/components/StatusBadge.test.tsx` | StatusBadge component tests |
| Create | `dashboard/src/components/SeverityBadge.test.tsx` | SeverityBadge component tests |
| Create | `dashboard/src/components/Layout.test.tsx` | Layout component tests |
| Create | `dashboard/src/pages/PrQueuePage.test.tsx` | PR Queue page tests |
| Create | `dashboard/src/pages/SessionsPage.test.tsx` | Sessions list page tests |
| Create | `dashboard/src/pages/SessionDetailPage.test.tsx` | Session detail page tests |
| Create | `dashboard/src/pages/AnalyticsPage.test.tsx` | Analytics page tests |
| Modify | `dashboard/package.json` | Add test deps + scripts |

---

### Task 1: Install Dependencies and Configure Vitest

**Files:**
- Modify: `dashboard/package.json`
- Create: `dashboard/vitest.config.ts`
- Create: `dashboard/src/test/setup.ts`

- [ ] **Step 1: Install test dependencies**

Run from `dashboard/`:
```bash
npm install -D vitest @vitest/coverage-v8 @testing-library/react @testing-library/jest-dom @testing-library/user-event jsdom
```

Expected: packages added to devDependencies in package.json, lock file updated.

- [ ] **Step 2: Add test scripts to package.json**

Add these scripts to `dashboard/package.json`:
```json
"test": "vitest run",
"test:watch": "vitest",
"test:coverage": "vitest run --coverage"
```

- [ ] **Step 3: Create vitest.config.ts**

Create `dashboard/vitest.config.ts`:
```typescript
import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      setupFiles: ["./src/test/setup.ts"],
      coverage: {
        provider: "v8",
        include: ["src/**/*.{ts,tsx}"],
        exclude: ["src/main.tsx", "src/vite-env.d.ts", "src/test/**"],
        thresholds: {
          lines: 80,
          branches: 80,
          functions: 80,
        },
      },
    },
  }),
);
```

- [ ] **Step 4: Create test setup file**

Create `dashboard/src/test/setup.ts`:
```typescript
import "@testing-library/jest-dom/vitest";
```

- [ ] **Step 5: Verify Vitest runs (no tests yet)**

Run from `dashboard/`:
```bash
npx vitest run
```

Expected: "No test files found" or similar — no errors about config/setup.

- [ ] **Step 6: Commit**

```bash
git add dashboard/package.json dashboard/package-lock.json dashboard/vitest.config.ts dashboard/src/test/setup.ts
git commit -m "test: add vitest infrastructure for dashboard"
```

---

### Task 2: Create Test Utilities (render wrapper + factories)

**Files:**
- Create: `dashboard/src/test/render.tsx`
- Create: `dashboard/src/test/factories.ts`

- [ ] **Step 1: Create custom render wrapper**

Create `dashboard/src/test/render.tsx`:
```tsx
import { render, type RenderOptions } from "@testing-library/react";
import { MemoryRouter, type MemoryRouterProps } from "react-router-dom";
import type { ReactElement } from "react";

interface CustomRenderOptions extends Omit<RenderOptions, "wrapper"> {
  initialEntries?: MemoryRouterProps["initialEntries"];
}

function customRender(ui: ReactElement, options: CustomRenderOptions = {}) {
  const { initialEntries = ["/"], ...renderOptions } = options;
  return render(ui, {
    wrapper: ({ children }) => (
      <MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter>
    ),
    ...renderOptions,
  });
}

export { customRender as render };
export { screen, waitFor, within, act } from "@testing-library/react";
export { default as userEvent } from "@testing-library/user-event";
```

- [ ] **Step 2: Create mock data factories**

Create `dashboard/src/test/factories.ts`:
```typescript
import type {
  Session,
  ConversationTurn,
  ToolExecution,
  FileChange,
  GovernanceViolation,
  ArchitectAnnotation,
  PullRequestReview,
  PullRequestDetail,
  LinkedSession,
  TeamAnalytics,
  DeveloperAnalytics,
  DeveloperMetrics,
} from "../types";

export function buildSession(overrides?: Partial<Session>): Session {
  return {
    id: "sess-001",
    developerEmail: "dev@example.com",
    repoPath: "org/repo",
    status: "Completed",
    startedAt: "2026-05-27T08:00:00Z",
    endedAt: "2026-05-27T09:30:00Z",
    createdAt: "2026-05-27T08:00:00Z",
    conversationTurns: [],
    toolExecutions: [],
    fileChanges: [],
    governanceViolations: [],
    architectAnnotations: [],
    ...overrides,
  };
}

export function buildConversationTurn(overrides?: Partial<ConversationTurn>): ConversationTurn {
  return {
    id: "turn-001",
    sessionId: "sess-001",
    turnIndex: 0,
    role: "user",
    content: "Hello, please help me.",
    tokenCount: 42,
    timestamp: "2026-05-27T08:01:00Z",
    isDecision: false,
    ...overrides,
  };
}

export function buildToolExecution(overrides?: Partial<ToolExecution>): ToolExecution {
  return {
    id: "tool-001",
    sessionId: "sess-001",
    toolName: "Read",
    inputParams: '{"path":"src/main.ts"}',
    outputSummary: "File contents returned",
    durationMs: 150,
    timestamp: "2026-05-27T08:02:00Z",
    fileChanges: [],
    ...overrides,
  };
}

export function buildFileChange(overrides?: Partial<FileChange>): FileChange {
  return {
    id: "fc-001",
    sessionId: "sess-001",
    filePath: "src/utils.ts",
    changeType: "Modified",
    diffText: "@@ -1,3 +1,4 @@\n+import { foo } from './foo';",
    ...overrides,
  };
}

export function buildGovernanceViolation(overrides?: Partial<GovernanceViolation>): GovernanceViolation {
  return {
    id: "viol-001",
    sessionId: "sess-001",
    ruleId: "no-console-log",
    severity: "Warning",
    message: "console.log found in production code",
    resolved: false,
    createdAt: "2026-05-27T09:00:00Z",
    ...overrides,
  };
}

export function buildArchitectAnnotation(overrides?: Partial<ArchitectAnnotation>): ArchitectAnnotation {
  return {
    id: "ann-001",
    sessionId: "sess-001",
    content: "Good use of Result pattern here.",
    createdBy: "lead@example.com",
    createdAt: "2026-05-27T09:15:00Z",
    ...overrides,
  };
}

export function buildPullRequestReview(overrides?: Partial<PullRequestReview>): PullRequestReview {
  return {
    id: "pr-001",
    prNumber: 42,
    repo: "org/repo",
    status: "Pending",
    ...overrides,
  };
}

export function buildLinkedSession(overrides?: Partial<LinkedSession>): LinkedSession {
  return {
    sessionId: "sess-001",
    commitSha: "abc1234",
    confidence: 0.85,
    linkMethod: "file-change-matching",
    ...overrides,
  };
}

export function buildPullRequestDetail(overrides?: Partial<PullRequestDetail>): PullRequestDetail {
  return {
    id: "pr-001",
    prNumber: 42,
    repo: "org/repo",
    status: "Pending",
    linkedSessions: [],
    ...overrides,
  };
}

export function buildTeamAnalytics(overrides?: Partial<TeamAnalytics>): TeamAnalytics {
  return {
    totalSessions: 100,
    sessionsThisWeek: 15,
    totalPullRequests: 50,
    approvedPullRequests: 30,
    pendingPullRequests: 12,
    needsChangesPullRequests: 8,
    totalViolations: 25,
    violationsThisWeek: 3,
    developers: [],
    ...overrides,
  };
}

export function buildDeveloperMetrics(overrides?: Partial<DeveloperMetrics>): DeveloperMetrics {
  return {
    email: "dev@example.com",
    sessionCount: 10,
    violationCount: 2,
    commitCount: 15,
    ...overrides,
  };
}

export function buildDeveloperAnalytics(overrides?: Partial<DeveloperAnalytics>): DeveloperAnalytics {
  return {
    email: "dev@example.com",
    totalSessions: 10,
    sessionsThisWeek: 3,
    totalViolations: 5,
    criticalViolations: 0,
    errorViolations: 1,
    warningViolations: 3,
    infoViolations: 1,
    totalCommits: 15,
    recentRepos: ["org/repo"],
    ...overrides,
  };
}
```

- [ ] **Step 3: Verify TypeScript compiles**

Run from `dashboard/`:
```bash
npx tsc -b --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add dashboard/src/test/render.tsx dashboard/src/test/factories.ts
git commit -m "test: add render wrapper and mock data factories"
```

---

### Task 3: API Client Tests

**Files:**
- Create: `dashboard/src/api.test.ts`

- [ ] **Step 1: Write API client tests**

Create `dashboard/src/api.test.ts`:
```typescript
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { api } from "./api";

function mockFetch(body: unknown, ok = true, status = 200, statusText = "OK") {
  return vi.fn().mockResolvedValue({
    ok,
    status,
    statusText,
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(typeof body === "string" ? body : JSON.stringify(body)),
  });
}

describe("api", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  describe("sessions", () => {
    it("list() calls GET /api/sessions with no params", async () => {
      const data = [{ id: "s1" }];
      globalThis.fetch = mockFetch(data);
      const result = await api.sessions.list();
      expect(result).toEqual(data);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/sessions", undefined);
    });

    it("list(developer, repo) appends query params", async () => {
      globalThis.fetch = mockFetch([]);
      await api.sessions.list("dev@test.com", "org/repo");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/sessions?developer=dev%40test.com&repo=org%2Frepo",
        undefined,
      );
    });

    it("get(id) calls GET /api/sessions/{id}", async () => {
      const session = { id: "sess-123" };
      globalThis.fetch = mockFetch(session);
      const result = await api.sessions.get("sess-123");
      expect(result).toEqual(session);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/sessions/sess-123", undefined);
    });
  });

  describe("pullRequests", () => {
    it("list() calls GET /api/prs with no params", async () => {
      globalThis.fetch = mockFetch([]);
      await api.pullRequests.list();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs", undefined);
    });

    it("list(repo) appends repo param", async () => {
      globalThis.fetch = mockFetch([]);
      await api.pullRequests.list("org/repo");
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs?repo=org%2Frepo", undefined);
    });

    it("get(owner, repo, pr) calls GET /api/prs/{owner}/{repo}/{pr}", async () => {
      globalThis.fetch = mockFetch({ id: "pr-1" });
      await api.pullRequests.get("myorg", "myrepo", 42);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs/myorg/myrepo/42", undefined);
    });

    it("review() sends POST with JSON body", async () => {
      globalThis.fetch = mockFetch({ reviewId: "r1" });
      const body = { repo: "org/repo", prNumber: 42, status: "Approved" as const, reviewedBy: "me@test.com" };
      await api.pullRequests.review("myorg", "myrepo", 42, body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs/myorg/myrepo/42/review", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("analytics", () => {
    it("team() calls GET /api/analytics/team", async () => {
      globalThis.fetch = mockFetch({ totalSessions: 10 });
      await api.analytics.team();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/analytics/team", undefined);
    });

    it("developer(email) encodes email in URL", async () => {
      globalThis.fetch = mockFetch({ email: "dev@test.com" });
      await api.analytics.developer("dev@test.com");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/analytics/developer/dev%40test.com",
        undefined,
      );
    });
  });

  describe("governance", () => {
    it("violations(sessionId) calls GET with no severity param", async () => {
      globalThis.fetch = mockFetch([]);
      await api.governance.violations("sess-1");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/governance/violations/sess-1",
        undefined,
      );
    });

    it("violations(sessionId, severity) appends severity param", async () => {
      globalThis.fetch = mockFetch([]);
      await api.governance.violations("sess-1", "Critical");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/governance/violations/sess-1?severity=Critical",
        undefined,
      );
    });

    it("resolve() sends POST with JSON body", async () => {
      globalThis.fetch = mockFetch({ violationId: "v1" });
      const body = { resolvedBy: "admin@test.com", resolutionReason: "False positive" };
      await api.governance.resolve("viol-1", body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/governance/violations/viol-1/resolve", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("annotations", () => {
    it("create() sends POST with JSON body", async () => {
      globalThis.fetch = mockFetch({ annotationId: "a1" });
      const body = { sessionId: "s1", content: "Good work", createdBy: "lead@test.com" };
      await api.annotations.create(body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/annotations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("error handling", () => {
    it("throws on non-ok response with status and body", async () => {
      globalThis.fetch = mockFetch("Not Found", false, 404, "Not Found");
      await expect(api.sessions.list()).rejects.toThrow("404: Not Found");
    });

    it("propagates network errors", async () => {
      globalThis.fetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
      await expect(api.sessions.list()).rejects.toThrow("Failed to fetch");
    });
  });
});
```

- [ ] **Step 2: Run tests to verify they pass**

Run from `dashboard/`:
```bash
npx vitest run src/api.test.ts
```

Expected: all 15 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/api.test.ts
git commit -m "test: add API client unit tests"
```

---

### Task 4: StatusBadge Component Tests

**Files:**
- Create: `dashboard/src/components/StatusBadge.test.tsx`

- [ ] **Step 1: Write StatusBadge tests**

Create `dashboard/src/components/StatusBadge.test.tsx`:
```tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "../test/render";
import StatusBadge from "./StatusBadge";

describe("StatusBadge", () => {
  it("renders Pending with yellow styling", () => {
    render(<StatusBadge status="Pending" />);
    const badge = screen.getByText("Pending");
    expect(badge).toHaveClass("bg-yellow-100", "text-yellow-800");
  });

  it("renders Approved with green styling", () => {
    render(<StatusBadge status="Approved" />);
    const badge = screen.getByText("Approved");
    expect(badge).toHaveClass("bg-green-100", "text-green-800");
  });

  it('renders NeedsChanges as "Needs Changes" with red styling', () => {
    render(<StatusBadge status="NeedsChanges" />);
    const badge = screen.getByText("Needs Changes");
    expect(badge).toHaveClass("bg-red-100", "text-red-800");
  });

  it("renders Active with blue styling", () => {
    render(<StatusBadge status="Active" />);
    const badge = screen.getByText("Active");
    expect(badge).toHaveClass("bg-blue-100", "text-blue-800");
  });

  it("renders Completed with gray styling", () => {
    render(<StatusBadge status="Completed" />);
    const badge = screen.getByText("Completed");
    expect(badge).toHaveClass("bg-gray-100", "text-gray-800");
  });

  it("renders Failed with red styling", () => {
    render(<StatusBadge status="Failed" />);
    const badge = screen.getByText("Failed");
    expect(badge).toHaveClass("bg-red-100", "text-red-800");
  });

  it("renders unknown status with gray fallback", () => {
    render(<StatusBadge status="SomethingElse" />);
    const badge = screen.getByText("SomethingElse");
    expect(badge).toHaveClass("bg-gray-100", "text-gray-800");
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/components/StatusBadge.test.tsx
```

Expected: all 7 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/components/StatusBadge.test.tsx
git commit -m "test: add StatusBadge component tests"
```

---

### Task 5: SeverityBadge Component Tests

**Files:**
- Create: `dashboard/src/components/SeverityBadge.test.tsx`

- [ ] **Step 1: Write SeverityBadge tests**

Create `dashboard/src/components/SeverityBadge.test.tsx`:
```tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "../test/render";
import SeverityBadge from "./SeverityBadge";

describe("SeverityBadge", () => {
  it("renders Critical with dark red styling", () => {
    render(<SeverityBadge severity="Critical" />);
    const badge = screen.getByText("Critical");
    expect(badge).toHaveClass("bg-red-600", "text-white");
  });

  it("renders Error with light red styling", () => {
    render(<SeverityBadge severity="Error" />);
    const badge = screen.getByText("Error");
    expect(badge).toHaveClass("bg-red-100", "text-red-800");
  });

  it("renders Warning with yellow styling", () => {
    render(<SeverityBadge severity="Warning" />);
    const badge = screen.getByText("Warning");
    expect(badge).toHaveClass("bg-yellow-100", "text-yellow-800");
  });

  it("renders Info with blue styling", () => {
    render(<SeverityBadge severity="Info" />);
    const badge = screen.getByText("Info");
    expect(badge).toHaveClass("bg-blue-100", "text-blue-800");
  });

  it("renders unknown severity with gray fallback", () => {
    render(<SeverityBadge severity="Unknown" />);
    const badge = screen.getByText("Unknown");
    expect(badge).toHaveClass("bg-gray-100", "text-gray-800");
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/components/SeverityBadge.test.tsx
```

Expected: all 5 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/components/SeverityBadge.test.tsx
git commit -m "test: add SeverityBadge component tests"
```

---

### Task 6: Layout Component Tests

**Files:**
- Create: `dashboard/src/components/Layout.test.tsx`

- [ ] **Step 1: Write Layout tests**

Create `dashboard/src/components/Layout.test.tsx`:
```tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "../test/render";
import Layout from "./Layout";

describe("Layout", () => {
  it("renders the app title", () => {
    render(<Layout />);
    expect(screen.getByText("Developer Overwatch")).toBeInTheDocument();
  });

  it("renders PR Queue link pointing to /", () => {
    render(<Layout />);
    const link = screen.getByRole("link", { name: "PR Queue" });
    expect(link).toHaveAttribute("href", "/");
  });

  it("renders Sessions link pointing to /sessions", () => {
    render(<Layout />);
    const link = screen.getByRole("link", { name: "Sessions" });
    expect(link).toHaveAttribute("href", "/sessions");
  });

  it("renders Analytics link pointing to /analytics", () => {
    render(<Layout />);
    const link = screen.getByRole("link", { name: "Analytics" });
    expect(link).toHaveAttribute("href", "/analytics");
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/components/Layout.test.tsx
```

Expected: all 4 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/components/Layout.test.tsx
git commit -m "test: add Layout component tests"
```

---

### Task 7: PrQueuePage Tests

**Files:**
- Create: `dashboard/src/pages/PrQueuePage.test.tsx`

- [ ] **Step 1: Write PrQueuePage tests**

Create `dashboard/src/pages/PrQueuePage.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import PrQueuePage from "./PrQueuePage";
import { api } from "../api";
import { buildPullRequestReview } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    pullRequests: {
      list: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("PrQueuePage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.pullRequests.list.mockReturnValue(new Promise(() => {}));
    render(<PrQueuePage />);
    expect(screen.getByText("Loading pull requests...")).toBeInTheDocument();
  });

  it("calls api.pullRequests.list() on mount", async () => {
    mockApi.pullRequests.list.mockResolvedValue([]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(mockApi.pullRequests.list).toHaveBeenCalledTimes(1);
    });
  });

  it("renders metric cards with correct counts", async () => {
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", status: "Pending" }),
      buildPullRequestReview({ id: "2", status: "Approved" }),
      buildPullRequestReview({ id: "3", status: "NeedsChanges" }),
      buildPullRequestReview({ id: "4", status: "Pending" }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("4")).toBeInTheDocument();
    });
    expect(screen.getByText("Total PRs")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
    expect(screen.getByText("1").closest("div")).toBeTruthy();
  });

  it("renders PR table rows", async () => {
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 42, repo: "org/repo", status: "Pending", reviewedBy: "reviewer@test.com" }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("#42")).toBeInTheDocument();
    });
    expect(screen.getByText("org/repo")).toBeInTheDocument();
    expect(screen.getByText("reviewer@test.com")).toBeInTheDocument();
  });

  it("shows empty state when no PRs", async () => {
    mockApi.pullRequests.list.mockResolvedValue([]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("No pull requests found.")).toBeInTheDocument();
    });
  });

  it("shows error state on fetch failure", async () => {
    mockApi.pullRequests.list.mockRejectedValue(new Error("Network error"));
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("Error: Network error")).toBeInTheDocument();
    });
  });

  it("sorts by PR number on header click", async () => {
    const user = userEvent.setup();
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 10 }),
      buildPullRequestReview({ id: "2", prNumber: 5 }),
      buildPullRequestReview({ id: "3", prNumber: 20 }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("#10")).toBeInTheDocument();
    });
    const prHeader = screen.getByText(/^PR #/);
    await user.click(prHeader);
    const cells = screen.getAllByText(/^#\d+$/);
    const numbers = cells.map((c) => parseInt(c.textContent!.slice(1)));
    expect(numbers).toEqual([5, 10, 20]);
  });

  it("filters by status dropdown", async () => {
    const user = userEvent.setup();
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 1, status: "Pending" }),
      buildPullRequestReview({ id: "2", prNumber: 2, status: "Approved" }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("#1")).toBeInTheDocument();
    });
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "Approved");
    expect(screen.queryByText("#1")).not.toBeInTheDocument();
    expect(screen.getByText("#2")).toBeInTheDocument();
  });

  it("shows unreviewed PR with dash for reviewer", async () => {
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 1, reviewedBy: undefined }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("—")).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/pages/PrQueuePage.test.tsx
```

Expected: all 9 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/PrQueuePage.test.tsx
git commit -m "test: add PrQueuePage tests"
```

---

### Task 8: SessionsPage Tests

**Files:**
- Create: `dashboard/src/pages/SessionsPage.test.tsx`

- [ ] **Step 1: Write SessionsPage tests**

Create `dashboard/src/pages/SessionsPage.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import SessionsPage from "./SessionsPage";
import { api } from "../api";
import { buildSession } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    sessions: {
      list: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("SessionsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.sessions.list.mockReturnValue(new Promise(() => {}));
    render(<SessionsPage />);
    expect(screen.getByText("Loading sessions...")).toBeInTheDocument();
  });

  it("renders session table rows with links", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "abcdef12-3456-7890-abcd-ef1234567890", developerEmail: "alice@test.com", repoPath: "org/app" }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("abcdef12...")).toBeInTheDocument();
    });
    const link = screen.getByRole("link", { name: "abcdef12..." });
    expect(link).toHaveAttribute("href", "/sessions/abcdef12-3456-7890-abcd-ef1234567890");
    expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    expect(screen.getByText("org/app")).toBeInTheDocument();
  });

  it("shows 'In progress' for active sessions without endedAt", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-active-00000000", status: "Active", endedAt: undefined }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("In progress")).toBeInTheDocument();
    });
  });

  it("shows formatted duration for completed sessions", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({
        id: "sess-done-000000000",
        startedAt: "2026-05-27T08:00:00Z",
        endedAt: "2026-05-27T09:30:00Z",
      }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("1h 30m")).toBeInTheDocument();
    });
  });

  it("filters by developer email", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-a-0000000000000", developerEmail: "alice@test.com" }),
      buildSession({ id: "sess-b-0000000000000", developerEmail: "bob@test.com" }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    });
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "alice@test.com");
    expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    expect(screen.queryByText("bob@test.com")).not.toBeInTheDocument();
  });

  it("shows empty state when no sessions", async () => {
    mockApi.sessions.list.mockResolvedValue([]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("No sessions found.")).toBeInTheDocument();
    });
  });

  it("shows error state on fetch failure", async () => {
    mockApi.sessions.list.mockRejectedValue(new Error("Server down"));
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("Error: Server down")).toBeInTheDocument();
    });
  });

  it("shows dash for sessions without repoPath", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-norepo-00000000", repoPath: undefined }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("—")).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/pages/SessionsPage.test.tsx
```

Expected: all 8 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/SessionsPage.test.tsx
git commit -m "test: add SessionsPage tests"
```

---

### Task 9: SessionDetailPage Tests

**Files:**
- Create: `dashboard/src/pages/SessionDetailPage.test.tsx`

- [ ] **Step 1: Write SessionDetailPage tests**

Create `dashboard/src/pages/SessionDetailPage.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import { Routes, Route } from "react-router-dom";
import SessionDetailPage from "./SessionDetailPage";
import { api } from "../api";
import {
  buildSession,
  buildConversationTurn,
  buildGovernanceViolation,
  buildFileChange,
  buildArchitectAnnotation,
} from "../test/factories";

vi.mock("../api", () => ({
  api: {
    sessions: {
      get: vi.fn(),
    },
    annotations: {
      create: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

function renderWithRoute(sessionId: string) {
  return render(
    <Routes>
      <Route path="/sessions/:sessionId" element={<SessionDetailPage />} />
    </Routes>,
    { initialEntries: [`/sessions/${sessionId}`] },
  );
}

describe("SessionDetailPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.sessions.get.mockReturnValue(new Promise(() => {}));
    renderWithRoute("sess-001");
    expect(screen.getByText("Loading session...")).toBeInTheDocument();
  });

  it("shows error state on fetch failure", async () => {
    mockApi.sessions.get.mockRejectedValue(new Error("Not found"));
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Error: Not found")).toBeInTheDocument();
    });
  });

  it("renders session header metadata", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        id: "abcdef123456-rest-of-id",
        repoPath: "org/my-repo",
        status: "Completed",
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0 }),
          buildConversationTurn({ id: "t2", turnIndex: 1 }),
        ],
      }),
    );
    renderWithRoute("abcdef123456-rest-of-id");
    await waitFor(() => {
      expect(screen.getByText("abcdef123456...")).toBeInTheDocument();
    });
    expect(screen.getByText("org/my-repo")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("renders back link to sessions list", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSession());
    renderWithRoute("sess-001");
    await waitFor(() => {
      const link = screen.getByRole("link", { name: /Back to Sessions/ });
      expect(link).toHaveAttribute("href", "/sessions");
    });
  });

  it("renders governance violations with severity badges", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        governanceViolations: [
          buildGovernanceViolation({ id: "v1", ruleId: "no-console", severity: "Warning", message: "console.log found" }),
          buildGovernanceViolation({ id: "v2", ruleId: "no-secrets", severity: "Critical", message: "Secret exposed", resolved: true, resolvedBy: "admin", resolutionReason: "False positive" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Governance Violations (2)")).toBeInTheDocument();
    });
    expect(screen.getByText("no-console")).toBeInTheDocument();
    expect(screen.getByText("console.log found")).toBeInTheDocument();
    expect(screen.getByText("Warning")).toBeInTheDocument();
    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText(/Resolved by admin: False positive/)).toBeInTheDocument();
  });

  it("renders conversation timeline with role-based styling", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, role: "user", content: "Fix the bug" }),
          buildConversationTurn({ id: "t2", turnIndex: 1, role: "assistant", content: "Looking at it now" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Fix the bug")).toBeInTheDocument();
    });
    expect(screen.getByText("Looking at it now")).toBeInTheDocument();
    expect(screen.getByText("user")).toBeInTheDocument();
    expect(screen.getByText("assistant")).toBeInTheDocument();
  });

  it("highlights decision turns", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, isDecision: true, content: "Decided to use Result pattern" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Decision")).toBeInTheDocument();
    });
  });

  it("shows token counts on turns", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, tokenCount: 150 }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("150 tokens")).toBeInTheDocument();
    });
  });

  it("renders file changes with diff text", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        fileChanges: [
          buildFileChange({ id: "fc1", filePath: "src/app.ts", changeType: "Modified", diffText: "+new line" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("src/app.ts")).toBeInTheDocument();
    });
    expect(screen.getByText("Modified")).toBeInTheDocument();
    expect(screen.getByText("+new line")).toBeInTheDocument();
  });

  it("renders existing annotations", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        architectAnnotations: [
          buildArchitectAnnotation({ id: "a1", content: "Solid approach", createdBy: "lead@test.com" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Solid approach")).toBeInTheDocument();
    });
    expect(screen.getByText("lead@test.com")).toBeInTheDocument();
  });

  it("submits annotation form and refetches session", async () => {
    const user = userEvent.setup();
    const initialSession = buildSession({ architectAnnotations: [] });
    const updatedSession = buildSession({
      architectAnnotations: [
        buildArchitectAnnotation({ content: "New note", createdBy: "me@test.com" }),
      ],
    });
    mockApi.sessions.get.mockResolvedValueOnce(initialSession);
    mockApi.annotations.create.mockResolvedValue({ annotationId: "a-new" });
    mockApi.sessions.get.mockResolvedValueOnce(updatedSession);

    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByPlaceholderText("Your email")).toBeInTheDocument();
    });

    await user.type(screen.getByPlaceholderText("Your email"), "me@test.com");
    await user.type(screen.getByPlaceholderText("Add annotation..."), "New note");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => {
      expect(mockApi.annotations.create).toHaveBeenCalledWith({
        sessionId: "sess-001",
        content: "New note",
        createdBy: "me@test.com",
      });
    });
    await waitFor(() => {
      expect(mockApi.sessions.get).toHaveBeenCalledTimes(2);
    });
    await waitFor(() => {
      expect(screen.getByText("New note")).toBeInTheDocument();
    });
  });

  it("shows submitting state on annotation button", async () => {
    const user = userEvent.setup();
    mockApi.sessions.get.mockResolvedValue(buildSession());
    mockApi.annotations.create.mockReturnValue(new Promise(() => {}));

    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByPlaceholderText("Your email")).toBeInTheDocument();
    });

    await user.type(screen.getByPlaceholderText("Your email"), "me@test.com");
    await user.type(screen.getByPlaceholderText("Add annotation..."), "A note");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => {
      expect(screen.getByText("Adding...")).toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "Adding..." })).toBeDisabled();
  });

  it("shows empty conversation timeline message", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({ conversationTurns: [] }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("No conversation turns recorded.")).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/pages/SessionDetailPage.test.tsx
```

Expected: all 14 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/SessionDetailPage.test.tsx
git commit -m "test: add SessionDetailPage tests"
```

---

### Task 10: AnalyticsPage Tests

**Files:**
- Create: `dashboard/src/pages/AnalyticsPage.test.tsx`

- [ ] **Step 1: Write AnalyticsPage tests**

Create `dashboard/src/pages/AnalyticsPage.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "../test/render";
import AnalyticsPage from "./AnalyticsPage";
import { api } from "../api";
import { buildTeamAnalytics, buildDeveloperMetrics } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    analytics: {
      team: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("AnalyticsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.analytics.team.mockReturnValue(new Promise(() => {}));
    render(<AnalyticsPage />);
    expect(screen.getByText("Loading analytics...")).toBeInTheDocument();
  });

  it("shows error state on fetch failure", async () => {
    mockApi.analytics.team.mockRejectedValue(new Error("Service unavailable"));
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("Error: Service unavailable")).toBeInTheDocument();
    });
  });

  it("renders metric cards with correct values", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        sessionsThisWeek: 15,
        pendingPullRequests: 12,
        violationsThisWeek: 3,
        totalSessions: 100,
        totalPullRequests: 50,
        totalViolations: 25,
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("Sessions This Week")).toBeInTheDocument();
    });
    expect(screen.getByText("15")).toBeInTheDocument();
    expect(screen.getByText("Pending PRs")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("Violations This Week")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("Total Sessions")).toBeInTheDocument();
    expect(screen.getByText("100")).toBeInTheDocument();
    expect(screen.getByText("Total Pull Requests")).toBeInTheDocument();
    expect(screen.getByText("Total Violations")).toBeInTheDocument();
  });

  it("calculates PR approval rate correctly", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        totalPullRequests: 50,
        approvedPullRequests: 30,
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("60%")).toBeInTheDocument();
    });
    expect(screen.getByText("PR Approval Rate")).toBeInTheDocument();
  });

  it("shows 0% approval rate when no PRs", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        totalPullRequests: 0,
        approvedPullRequests: 0,
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("0%")).toBeInTheDocument();
    });
  });

  it("renders developer breakdown table", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        developers: [
          buildDeveloperMetrics({ email: "alice@test.com", sessionCount: 10, commitCount: 20, violationCount: 0 }),
          buildDeveloperMetrics({ email: "bob@test.com", sessionCount: 5, commitCount: 8, violationCount: 3 }),
        ],
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    });
    expect(screen.getByText("bob@test.com")).toBeInTheDocument();
    expect(screen.getByText("Developer Breakdown")).toBeInTheDocument();
  });

  it("highlights violation count in red when > 0", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        developers: [
          buildDeveloperMetrics({ email: "dev@test.com", violationCount: 5 }),
        ],
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("dev@test.com")).toBeInTheDocument();
    });
    const violationCell = screen.getByText("5");
    expect(violationCell).toHaveClass("text-red-600", "font-medium");
  });

  it("does not highlight zero violations", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        developers: [
          buildDeveloperMetrics({ email: "clean@test.com", violationCount: 0 }),
        ],
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("clean@test.com")).toBeInTheDocument();
    });
    const zeroCell = screen.getByText("0");
    expect(zeroCell).not.toHaveClass("text-red-600");
  });

  it("shows empty developer data message", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({ developers: [] }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("No developer data.")).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run tests**

```bash
npx vitest run src/pages/AnalyticsPage.test.tsx
```

Expected: all 9 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/AnalyticsPage.test.tsx
git commit -m "test: add AnalyticsPage tests"
```

---

### Task 11: Run Full Suite with Coverage

**Files:** None (verification only)

- [ ] **Step 1: Run all tests**

```bash
cd dashboard && npx vitest run
```

Expected: all ~72 tests pass across 8 test files.

- [ ] **Step 2: Run coverage report**

```bash
cd dashboard && npx vitest run --coverage
```

Expected: lines >= 80%, branches >= 80%, functions >= 80%. If any threshold is below 80%, add targeted tests for uncovered branches (most likely in page components with conditional rendering).

- [ ] **Step 3: Run TypeScript check**

```bash
cd dashboard && npx tsc -b --noEmit
```

Expected: no type errors.

- [ ] **Step 4: Final commit**

```bash
git add -A dashboard/
git commit -m "test: dashboard test suite complete — 80%+ coverage"
```
