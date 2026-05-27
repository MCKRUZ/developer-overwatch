# Dashboard SPA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a React SPA dashboard that consumes the Developer Overwatch API, providing PR Queue, Session Review, and Team Analytics views.

**Architecture:** React 19 + TypeScript + Vite + Tailwind CSS v3. Simple fetch-based API client with Vite dev proxy to `http://localhost:5000`. React Router v7 for client-side routing. No state management library — local state + prop drilling for v1.

**Tech Stack:** React 19, TypeScript, Vite 6, Tailwind CSS 3, React Router 7

---

## File Structure

```
dashboard/
├── index.html
├── package.json
├── tsconfig.json
├── tsconfig.app.json
├── tsconfig.node.json
├── vite.config.ts
├── tailwind.config.js
├── postcss.config.js
├── Dockerfile
├── nginx.conf
├── src/
│   ├── main.tsx
│   ├── App.tsx
│   ├── index.css
│   ├── types.ts
│   ├── api.ts
│   ├── components/
│   │   ├── Layout.tsx
│   │   ├── StatusBadge.tsx
│   │   └── SeverityBadge.tsx
│   └── pages/
│       ├── PrQueuePage.tsx
│       ├── SessionsPage.tsx
│       ├── SessionDetailPage.tsx
│       └── AnalyticsPage.tsx
```

---

### Task 1: Scaffold React + Vite + Tailwind Project

**Files:**
- Create: `dashboard/package.json`
- Create: `dashboard/index.html`
- Create: `dashboard/tsconfig.json`
- Create: `dashboard/tsconfig.app.json`
- Create: `dashboard/tsconfig.node.json`
- Create: `dashboard/vite.config.ts`
- Create: `dashboard/tailwind.config.js`
- Create: `dashboard/postcss.config.js`
- Create: `dashboard/src/main.tsx`
- Create: `dashboard/src/index.css`
- Create: `dashboard/src/App.tsx`
- Create: `dashboard/src/vite-env.d.ts`

- [ ] **Step 1: Create package.json**

```json
{
  "name": "developer-overwatch-dashboard",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview",
    "typecheck": "tsc -b --noEmit"
  },
  "dependencies": {
    "react": "^19.1.0",
    "react-dom": "^19.1.0",
    "react-router-dom": "^7.6.1"
  },
  "devDependencies": {
    "@types/react": "^19.1.4",
    "@types/react-dom": "^19.1.5",
    "@vitejs/plugin-react": "^4.5.2",
    "autoprefixer": "^10.4.21",
    "postcss": "^8.5.4",
    "tailwindcss": "^3.4.17",
    "typescript": "^5.8.3",
    "vite": "^6.3.5"
  }
}
```

- [ ] **Step 2: Create index.html**

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Developer Overwatch</title>
  </head>
  <body class="bg-gray-50 text-gray-900">
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 3: Create TypeScript configs**

`tsconfig.json`:
```json
{
  "files": [],
  "references": [
    { "path": "./tsconfig.app.json" },
    { "path": "./tsconfig.node.json" }
  ]
}
```

`tsconfig.app.json`:
```json
{
  "compilerOptions": {
    "target": "ES2020",
    "useDefineForClassFields": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "isolatedModules": true,
    "moduleDetection": "force",
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "noUncheckedIndexedAccess": true
  },
  "include": ["src"]
}
```

`tsconfig.node.json`:
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2023"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "isolatedModules": true,
    "moduleDetection": "force",
    "noEmit": true,
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true
  },
  "include": ["vite.config.ts"]
}
```

- [ ] **Step 4: Create vite.config.ts with API proxy**

```ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
    },
  },
});
```

- [ ] **Step 5: Create Tailwind + PostCSS config**

`tailwind.config.js`:
```js
/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {},
  },
  plugins: [],
};
```

`postcss.config.js`:
```js
export default {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
};
```

- [ ] **Step 6: Create entry point files**

`src/vite-env.d.ts`:
```ts
/// <reference types="vite/client" />
```

`src/index.css`:
```css
@tailwind base;
@tailwind components;
@tailwind utilities;
```

`src/main.tsx`:
```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
```

`src/App.tsx`:
```tsx
import { Routes, Route } from "react-router-dom";

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<div>Developer Overwatch</div>} />
    </Routes>
  );
}
```

- [ ] **Step 7: Install dependencies and verify build**

```bash
cd dashboard && npm install && npm run typecheck && npm run build
```

Expected: `tsc` passes with 0 errors, `vite build` produces `dist/` output.

- [ ] **Step 8: Commit**

```bash
git add dashboard/
git commit -m "feat: scaffold dashboard SPA with React, Vite, Tailwind"
```

---

### Task 2: TypeScript Types + API Client

**Files:**
- Create: `dashboard/src/types.ts`
- Create: `dashboard/src/api.ts`

The API returns camelCase JSON. Enums are serialized as strings (`"Active"`, `"Pending"`, etc.). Nulls are omitted from responses.

- [ ] **Step 1: Create types.ts**

```ts
export interface Session {
  id: string;
  developerEmail: string;
  developerName?: string;
  repoPath?: string;
  status: "Active" | "Completed" | "Failed";
  startedAt: string;
  endedAt?: string;
  summaryJson?: string;
  createdAt: string;
  conversationTurns: ConversationTurn[];
  toolExecutions: ToolExecution[];
  fileChanges: FileChange[];
  governanceViolations: GovernanceViolation[];
  architectAnnotations: ArchitectAnnotation[];
}

export interface ConversationTurn {
  id: string;
  sessionId: string;
  turnIndex: number;
  role: string;
  content?: string;
  tokenCount?: number;
  timestamp: string;
  isDecision: boolean;
}

export interface ToolExecution {
  id: string;
  sessionId: string;
  turnId?: string;
  toolName: string;
  inputParams?: string;
  outputSummary?: string;
  durationMs?: number;
  timestamp: string;
  fileChanges: FileChange[];
}

export interface FileChange {
  id: string;
  sessionId: string;
  toolExecutionId?: string;
  filePath: string;
  beforeHash?: string;
  afterHash?: string;
  diffText?: string;
  changeType?: "Created" | "Modified" | "Deleted";
}

export interface GovernanceViolation {
  id: string;
  sessionId: string;
  ruleId: string;
  severity: "Info" | "Warning" | "Error" | "Critical";
  message: string;
  contextJson?: string;
  resolved: boolean;
  resolvedBy?: string;
  resolutionReason?: string;
  resolvedAt?: string;
  createdAt: string;
}

export interface ArchitectAnnotation {
  id: string;
  sessionId: string;
  turnId?: string;
  toolExecutionId?: string;
  content: string;
  createdBy: string;
  createdAt: string;
}

export interface PullRequestReview {
  id: string;
  prNumber: number;
  repo: string;
  status: "Pending" | "Approved" | "NeedsChanges";
  reviewedBy?: string;
  reviewedAt?: string;
  notes?: string;
}

export interface PullRequestDetail {
  id: string;
  prNumber: number;
  repo: string;
  status: string;
  reviewedBy?: string;
  reviewedAt?: string;
  notes?: string;
  linkedSessions: LinkedSession[];
}

export interface LinkedSession {
  sessionId: string;
  commitSha: string;
  confidence: number;
  linkMethod: string;
  developerEmail?: string;
  sessionStartedAt?: string;
  sessionEndedAt?: string;
}

export interface TeamAnalytics {
  totalSessions: number;
  sessionsThisWeek: number;
  totalPullRequests: number;
  approvedPullRequests: number;
  pendingPullRequests: number;
  needsChangesPullRequests: number;
  totalViolations: number;
  violationsThisWeek: number;
  developers: DeveloperMetrics[];
}

export interface DeveloperMetrics {
  email: string;
  sessionCount: number;
  violationCount: number;
  commitCount: number;
}

export interface DeveloperAnalytics {
  email: string;
  totalSessions: number;
  sessionsThisWeek: number;
  totalViolations: number;
  criticalViolations: number;
  errorViolations: number;
  warningViolations: number;
  infoViolations: number;
  totalCommits: number;
  recentRepos: string[];
}
```

- [ ] **Step 2: Create api.ts**

```ts
import type {
  Session,
  PullRequestReview,
  PullRequestDetail,
  TeamAnalytics,
  DeveloperAnalytics,
  GovernanceViolation,
} from "./types";

const BASE = "/api";

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`${res.status}: ${body}`);
  }
  return res.json() as Promise<T>;
}

export const api = {
  sessions: {
    list(developer?: string, repo?: string): Promise<Session[]> {
      const params = new URLSearchParams();
      if (developer) params.set("developer", developer);
      if (repo) params.set("repo", repo);
      const qs = params.toString();
      return fetchJson(`${BASE}/sessions${qs ? `?${qs}` : ""}`);
    },
    get(id: string): Promise<Session> {
      return fetchJson(`${BASE}/sessions/${id}`);
    },
  },

  pullRequests: {
    list(repo?: string): Promise<PullRequestReview[]> {
      const qs = repo ? `?repo=${encodeURIComponent(repo)}` : "";
      return fetchJson(`${BASE}/prs${qs}`);
    },
    get(owner: string, repoName: string, prNumber: number): Promise<PullRequestDetail> {
      return fetchJson(`${BASE}/prs/${owner}/${repoName}/${prNumber}`);
    },
    review(
      owner: string,
      repoName: string,
      prNumber: number,
      body: { repo: string; prNumber: number; status: string; reviewedBy: string; notes?: string },
    ): Promise<{ reviewId: string }> {
      return fetchJson(`${BASE}/prs/${owner}/${repoName}/${prNumber}/review`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    },
  },

  analytics: {
    team(): Promise<TeamAnalytics> {
      return fetchJson(`${BASE}/analytics/team`);
    },
    developer(email: string): Promise<DeveloperAnalytics> {
      return fetchJson(`${BASE}/analytics/developer/${encodeURIComponent(email)}`);
    },
  },

  governance: {
    violations(sessionId: string, severity?: string): Promise<GovernanceViolation[]> {
      const qs = severity ? `?severity=${severity}` : "";
      return fetchJson(`${BASE}/governance/violations/${sessionId}${qs}`);
    },
    resolve(
      violationId: string,
      body: { resolvedBy: string; resolutionReason: string },
    ): Promise<void> {
      return fetchJson(`${BASE}/governance/violations/${violationId}/resolve`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    },
  },

  annotations: {
    create(body: {
      sessionId: string;
      content: string;
      createdBy: string;
      turnId?: string;
      toolExecutionId?: string;
    }): Promise<{ annotationId: string }> {
      return fetchJson(`${BASE}/annotations`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    },
  },
};
```

- [ ] **Step 3: Verify typecheck passes**

```bash
cd dashboard && npm run typecheck
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add dashboard/src/types.ts dashboard/src/api.ts
git commit -m "feat: add TypeScript types and API client for dashboard"
```

---

### Task 3: Layout Shell + Routing

**Files:**
- Create: `dashboard/src/components/Layout.tsx`
- Create: `dashboard/src/components/StatusBadge.tsx`
- Create: `dashboard/src/components/SeverityBadge.tsx`
- Create: `dashboard/src/pages/PrQueuePage.tsx` (placeholder)
- Create: `dashboard/src/pages/SessionsPage.tsx` (placeholder)
- Create: `dashboard/src/pages/SessionDetailPage.tsx` (placeholder)
- Create: `dashboard/src/pages/AnalyticsPage.tsx` (placeholder)
- Modify: `dashboard/src/App.tsx`

- [ ] **Step 1: Create Layout.tsx**

```tsx
import { NavLink, Outlet } from "react-router-dom";

const navItems = [
  { to: "/", label: "PR Queue" },
  { to: "/sessions", label: "Sessions" },
  { to: "/analytics", label: "Analytics" },
];

export default function Layout() {
  return (
    <div className="min-h-screen flex flex-col">
      <header className="bg-gray-900 text-white">
        <div className="max-w-7xl mx-auto px-4 py-3 flex items-center justify-between">
          <h1 className="text-lg font-semibold tracking-tight">Developer Overwatch</h1>
          <nav className="flex gap-1">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === "/"}
                className={({ isActive }) =>
                  `px-3 py-1.5 rounded text-sm font-medium transition-colors ${
                    isActive
                      ? "bg-gray-700 text-white"
                      : "text-gray-300 hover:text-white hover:bg-gray-800"
                  }`
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
        </div>
      </header>
      <main className="flex-1 max-w-7xl mx-auto w-full px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
```

- [ ] **Step 2: Create StatusBadge.tsx**

```tsx
const statusStyles: Record<string, string> = {
  Pending: "bg-yellow-100 text-yellow-800",
  Approved: "bg-green-100 text-green-800",
  NeedsChanges: "bg-red-100 text-red-800",
  Active: "bg-blue-100 text-blue-800",
  Completed: "bg-gray-100 text-gray-800",
  Failed: "bg-red-100 text-red-800",
};

export default function StatusBadge({ status }: { status: string }) {
  const style = statusStyles[status] ?? "bg-gray-100 text-gray-800";
  const label = status === "NeedsChanges" ? "Needs Changes" : status;
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${style}`}>
      {label}
    </span>
  );
}
```

- [ ] **Step 3: Create SeverityBadge.tsx**

```tsx
const severityStyles: Record<string, string> = {
  Critical: "bg-red-600 text-white",
  Error: "bg-red-100 text-red-800",
  Warning: "bg-yellow-100 text-yellow-800",
  Info: "bg-blue-100 text-blue-800",
};

export default function SeverityBadge({ severity }: { severity: string }) {
  const style = severityStyles[severity] ?? "bg-gray-100 text-gray-800";
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${style}`}>
      {severity}
    </span>
  );
}
```

- [ ] **Step 4: Create placeholder pages**

`src/pages/PrQueuePage.tsx`:
```tsx
export default function PrQueuePage() {
  return <div>PR Queue — loading...</div>;
}
```

`src/pages/SessionsPage.tsx`:
```tsx
export default function SessionsPage() {
  return <div>Sessions — loading...</div>;
}
```

`src/pages/SessionDetailPage.tsx`:
```tsx
export default function SessionDetailPage() {
  return <div>Session Detail — loading...</div>;
}
```

`src/pages/AnalyticsPage.tsx`:
```tsx
export default function AnalyticsPage() {
  return <div>Analytics — loading...</div>;
}
```

- [ ] **Step 5: Update App.tsx with routing**

```tsx
import { Routes, Route } from "react-router-dom";
import Layout from "./components/Layout";
import PrQueuePage from "./pages/PrQueuePage";
import SessionsPage from "./pages/SessionsPage";
import SessionDetailPage from "./pages/SessionDetailPage";
import AnalyticsPage from "./pages/AnalyticsPage";

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<PrQueuePage />} />
        <Route path="/sessions" element={<SessionsPage />} />
        <Route path="/sessions/:sessionId" element={<SessionDetailPage />} />
        <Route path="/analytics" element={<AnalyticsPage />} />
      </Route>
    </Routes>
  );
}
```

- [ ] **Step 6: Verify typecheck and build**

```bash
cd dashboard && npm run typecheck && npm run build
```

Expected: 0 errors, clean build.

- [ ] **Step 7: Commit**

```bash
git add dashboard/src/
git commit -m "feat: add layout shell, routing, and status/severity badge components"
```

---

### Task 4: PR Queue Page

**Files:**
- Modify: `dashboard/src/pages/PrQueuePage.tsx`

- [ ] **Step 1: Implement PrQueuePage.tsx**

```tsx
import { useEffect, useState } from "react";
import { api } from "../api";
import type { PullRequestReview } from "../types";
import StatusBadge from "../components/StatusBadge";

type SortField = "prNumber" | "repo" | "status";
type FilterStatus = "" | "Pending" | "Approved" | "NeedsChanges";

export default function PrQueuePage() {
  const [prs, setPrs] = useState<PullRequestReview[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sortField, setSortField] = useState<SortField>("prNumber");
  const [sortAsc, setSortAsc] = useState(false);
  const [filterStatus, setFilterStatus] = useState<FilterStatus>("");

  useEffect(() => {
    api.pullRequests
      .list()
      .then(setPrs)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-gray-500">Loading pull requests...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;

  const filtered = filterStatus ? prs.filter((pr) => pr.status === filterStatus) : prs;

  const sorted = [...filtered].sort((a, b) => {
    const aVal = a[sortField];
    const bVal = b[sortField];
    if (aVal < bVal) return sortAsc ? -1 : 1;
    if (aVal > bVal) return sortAsc ? 1 : -1;
    return 0;
  });

  const counts = {
    total: prs.length,
    pending: prs.filter((p) => p.status === "Pending").length,
    approved: prs.filter((p) => p.status === "Approved").length,
    needsChanges: prs.filter((p) => p.status === "NeedsChanges").length,
  };

  function handleSort(field: SortField) {
    if (sortField === field) {
      setSortAsc(!sortAsc);
    } else {
      setSortField(field);
      setSortAsc(true);
    }
  }

  function sortIndicator(field: SortField) {
    if (sortField !== field) return "";
    return sortAsc ? " ↑" : " ↓";
  }

  return (
    <div>
      <h2 className="text-xl font-semibold mb-4">PR Queue</h2>

      <div className="grid grid-cols-4 gap-4 mb-6">
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{counts.total}</div>
          <div className="text-sm text-gray-500">Total PRs</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-yellow-600">{counts.pending}</div>
          <div className="text-sm text-gray-500">Pending</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-green-600">{counts.approved}</div>
          <div className="text-sm text-gray-500">Approved</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-red-600">{counts.needsChanges}</div>
          <div className="text-sm text-gray-500">Needs Changes</div>
        </div>
      </div>

      <div className="mb-4">
        <select
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as FilterStatus)}
          className="border rounded px-3 py-1.5 text-sm"
        >
          <option value="">All Statuses</option>
          <option value="Pending">Pending</option>
          <option value="Approved">Approved</option>
          <option value="NeedsChanges">Needs Changes</option>
        </select>
      </div>

      {sorted.length === 0 ? (
        <div className="text-gray-500 text-center py-8">No pull requests found.</div>
      ) : (
        <div className="bg-white rounded-lg border overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th
                  className="text-left px-4 py-3 font-medium cursor-pointer select-none"
                  onClick={() => handleSort("prNumber")}
                >
                  PR #{sortIndicator("prNumber")}
                </th>
                <th
                  className="text-left px-4 py-3 font-medium cursor-pointer select-none"
                  onClick={() => handleSort("repo")}
                >
                  Repository{sortIndicator("repo")}
                </th>
                <th
                  className="text-left px-4 py-3 font-medium cursor-pointer select-none"
                  onClick={() => handleSort("status")}
                >
                  Status{sortIndicator("status")}
                </th>
                <th className="text-left px-4 py-3 font-medium">Reviewed By</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {sorted.map((pr) => (
                <tr key={pr.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3 font-mono">#{pr.prNumber}</td>
                  <td className="px-4 py-3">{pr.repo}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={pr.status} />
                  </td>
                  <td className="px-4 py-3 text-gray-500">{pr.reviewedBy ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify typecheck**

```bash
cd dashboard && npm run typecheck
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/PrQueuePage.tsx
git commit -m "feat: implement PR Queue page with sorting and filtering"
```

---

### Task 5: Sessions List Page

**Files:**
- Modify: `dashboard/src/pages/SessionsPage.tsx`

- [ ] **Step 1: Implement SessionsPage.tsx**

```tsx
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import type { Session } from "../types";
import StatusBadge from "../components/StatusBadge";

export default function SessionsPage() {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterDev, setFilterDev] = useState("");

  useEffect(() => {
    api.sessions
      .list()
      .then(setSessions)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-gray-500">Loading sessions...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;

  const filtered = filterDev
    ? sessions.filter((s) => s.developerEmail.toLowerCase().includes(filterDev.toLowerCase()))
    : sessions;

  const sorted = [...filtered].sort(
    (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime(),
  );

  const developers = [...new Set(sessions.map((s) => s.developerEmail))];

  function formatDate(iso: string): string {
    return new Date(iso).toLocaleString();
  }

  function duration(start: string, end?: string): string {
    if (!end) return "In progress";
    const ms = new Date(end).getTime() - new Date(start).getTime();
    const mins = Math.round(ms / 60_000);
    if (mins < 60) return `${mins}m`;
    return `${Math.floor(mins / 60)}h ${mins % 60}m`;
  }

  return (
    <div>
      <h2 className="text-xl font-semibold mb-4">Sessions</h2>

      <div className="mb-4">
        <select
          value={filterDev}
          onChange={(e) => setFilterDev(e.target.value)}
          className="border rounded px-3 py-1.5 text-sm"
        >
          <option value="">All Developers</option>
          {developers.map((dev) => (
            <option key={dev} value={dev}>
              {dev}
            </option>
          ))}
        </select>
      </div>

      {sorted.length === 0 ? (
        <div className="text-gray-500 text-center py-8">No sessions found.</div>
      ) : (
        <div className="bg-white rounded-lg border overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Session</th>
                <th className="text-left px-4 py-3 font-medium">Developer</th>
                <th className="text-left px-4 py-3 font-medium">Repo</th>
                <th className="text-left px-4 py-3 font-medium">Status</th>
                <th className="text-left px-4 py-3 font-medium">Started</th>
                <th className="text-left px-4 py-3 font-medium">Duration</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {sorted.map((s) => (
                <tr key={s.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <Link
                      to={`/sessions/${s.id}`}
                      className="text-blue-600 hover:underline font-mono text-xs"
                    >
                      {s.id.slice(0, 8)}...
                    </Link>
                  </td>
                  <td className="px-4 py-3">{s.developerEmail}</td>
                  <td className="px-4 py-3 text-gray-500">{s.repoPath ?? "—"}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={s.status} />
                  </td>
                  <td className="px-4 py-3 text-gray-500">{formatDate(s.startedAt)}</td>
                  <td className="px-4 py-3 text-gray-500">{duration(s.startedAt, s.endedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify typecheck**

```bash
cd dashboard && npm run typecheck
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/SessionsPage.tsx
git commit -m "feat: implement Sessions list page with developer filter"
```

---

### Task 6: Session Detail Page

**Files:**
- Modify: `dashboard/src/pages/SessionDetailPage.tsx`

Shows the session timeline: conversation turns, tool executions, file changes, violations, and annotations. Annotations can be added via a form.

- [ ] **Step 1: Implement SessionDetailPage.tsx**

```tsx
import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { api } from "../api";
import type { Session } from "../types";
import StatusBadge from "../components/StatusBadge";
import SeverityBadge from "../components/SeverityBadge";

export default function SessionDetailPage() {
  const { sessionId } = useParams<{ sessionId: string }>();
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [annotationContent, setAnnotationContent] = useState("");
  const [annotationEmail, setAnnotationEmail] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!sessionId) return;
    api.sessions
      .get(sessionId)
      .then(setSession)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [sessionId]);

  if (loading) return <div className="text-gray-500">Loading session...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;
  if (!session) return <div className="text-red-600">Session not found</div>;

  async function handleAddAnnotation(e: React.FormEvent) {
    e.preventDefault();
    if (!sessionId || !annotationContent.trim() || !annotationEmail.trim()) return;
    setSubmitting(true);
    try {
      await api.annotations.create({
        sessionId,
        content: annotationContent,
        createdBy: annotationEmail,
      });
      const updated = await api.sessions.get(sessionId);
      setSession(updated);
      setAnnotationContent("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to add annotation");
    } finally {
      setSubmitting(false);
    }
  }

  const turns = [...session.conversationTurns].sort((a, b) => a.turnIndex - b.turnIndex);
  const violations = session.governanceViolations;
  const fileChanges = session.fileChanges;
  const annotations = session.architectAnnotations;

  function formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString();
  }

  return (
    <div>
      <div className="mb-6">
        <Link to="/sessions" className="text-blue-600 hover:underline text-sm">
          &larr; Back to Sessions
        </Link>
      </div>

      <div className="bg-white rounded-lg border p-6 mb-6">
        <div className="flex items-start justify-between">
          <div>
            <h2 className="text-xl font-semibold font-mono">{session.id.slice(0, 12)}...</h2>
            <p className="text-gray-500 mt-1">{session.developerEmail}</p>
          </div>
          <StatusBadge status={session.status} />
        </div>
        <div className="grid grid-cols-4 gap-4 mt-4 text-sm">
          <div>
            <span className="text-gray-500">Repo:</span>{" "}
            <span>{session.repoPath ?? "—"}</span>
          </div>
          <div>
            <span className="text-gray-500">Started:</span>{" "}
            <span>{new Date(session.startedAt).toLocaleString()}</span>
          </div>
          <div>
            <span className="text-gray-500">Ended:</span>{" "}
            <span>{session.endedAt ? new Date(session.endedAt).toLocaleString() : "In progress"}</span>
          </div>
          <div>
            <span className="text-gray-500">Turns:</span> <span>{turns.length}</span>
          </div>
        </div>
      </div>

      {violations.length > 0 && (
        <div className="bg-white rounded-lg border p-4 mb-6">
          <h3 className="font-semibold mb-3">Governance Violations ({violations.length})</h3>
          <div className="space-y-2">
            {violations.map((v) => (
              <div
                key={v.id}
                className={`flex items-start gap-3 p-3 rounded ${v.resolved ? "bg-gray-50 opacity-60" : "bg-red-50"}`}
              >
                <SeverityBadge severity={v.severity} />
                <div className="flex-1">
                  <div className="font-medium text-sm">{v.ruleId}</div>
                  <div className="text-sm text-gray-600">{v.message}</div>
                  {v.resolved && (
                    <div className="text-xs text-gray-400 mt-1">
                      Resolved by {v.resolvedBy}: {v.resolutionReason}
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="bg-white rounded-lg border p-4 mb-6">
        <h3 className="font-semibold mb-3">Conversation Timeline</h3>
        <div className="space-y-3">
          {turns.length === 0 ? (
            <div className="text-gray-500 text-sm">No conversation turns recorded.</div>
          ) : (
            turns.map((turn) => (
              <div
                key={turn.id}
                className={`p-3 rounded text-sm ${
                  turn.role === "user"
                    ? "bg-blue-50 border-l-4 border-blue-400"
                    : turn.role === "assistant"
                      ? "bg-gray-50 border-l-4 border-gray-400"
                      : "bg-yellow-50 border-l-4 border-yellow-400"
                } ${turn.isDecision ? "ring-2 ring-amber-300" : ""}`}
              >
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium capitalize">{turn.role}</span>
                  <span className="text-gray-400 text-xs">{formatTime(turn.timestamp)}</span>
                  {turn.isDecision && (
                    <span className="text-xs bg-amber-100 text-amber-800 px-1.5 py-0.5 rounded">
                      Decision
                    </span>
                  )}
                  {turn.tokenCount != null && (
                    <span className="text-xs text-gray-400">{turn.tokenCount} tokens</span>
                  )}
                </div>
                {turn.content && (
                  <pre className="whitespace-pre-wrap text-sm text-gray-700 max-h-40 overflow-y-auto">
                    {turn.content}
                  </pre>
                )}
              </div>
            ))
          )}
        </div>
      </div>

      {fileChanges.length > 0 && (
        <div className="bg-white rounded-lg border p-4 mb-6">
          <h3 className="font-semibold mb-3">File Changes ({fileChanges.length})</h3>
          <div className="space-y-2">
            {fileChanges.map((fc) => (
              <div key={fc.id} className="p-3 bg-gray-50 rounded">
                <div className="flex items-center gap-2 text-sm">
                  <span className="font-mono">{fc.filePath}</span>
                  {fc.changeType && (
                    <span
                      className={`text-xs px-1.5 py-0.5 rounded ${
                        fc.changeType === "Created"
                          ? "bg-green-100 text-green-700"
                          : fc.changeType === "Deleted"
                            ? "bg-red-100 text-red-700"
                            : "bg-blue-100 text-blue-700"
                      }`}
                    >
                      {fc.changeType}
                    </span>
                  )}
                </div>
                {fc.diffText && (
                  <pre className="mt-2 text-xs bg-gray-900 text-gray-100 p-3 rounded overflow-x-auto max-h-60 overflow-y-auto">
                    {fc.diffText}
                  </pre>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="bg-white rounded-lg border p-4 mb-6">
        <h3 className="font-semibold mb-3">
          Annotations {annotations.length > 0 && `(${annotations.length})`}
        </h3>
        {annotations.length > 0 && (
          <div className="space-y-2 mb-4">
            {annotations.map((a) => (
              <div key={a.id} className="p-3 bg-indigo-50 rounded text-sm">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium">{a.createdBy}</span>
                  <span className="text-gray-400 text-xs">
                    {new Date(a.createdAt).toLocaleString()}
                  </span>
                </div>
                <p className="text-gray-700">{a.content}</p>
              </div>
            ))}
          </div>
        )}
        <form onSubmit={handleAddAnnotation} className="flex gap-2">
          <input
            type="email"
            placeholder="Your email"
            value={annotationEmail}
            onChange={(e) => setAnnotationEmail(e.target.value)}
            className="border rounded px-3 py-1.5 text-sm w-48"
            required
          />
          <input
            type="text"
            placeholder="Add annotation..."
            value={annotationContent}
            onChange={(e) => setAnnotationContent(e.target.value)}
            className="border rounded px-3 py-1.5 text-sm flex-1"
            required
          />
          <button
            type="submit"
            disabled={submitting}
            className="bg-indigo-600 text-white px-4 py-1.5 rounded text-sm hover:bg-indigo-700 disabled:opacity-50"
          >
            {submitting ? "Adding..." : "Add"}
          </button>
        </form>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify typecheck**

```bash
cd dashboard && npm run typecheck
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/SessionDetailPage.tsx
git commit -m "feat: implement Session Detail page with timeline, violations, and annotations"
```

---

### Task 7: Team Analytics Page

**Files:**
- Modify: `dashboard/src/pages/AnalyticsPage.tsx`

- [ ] **Step 1: Implement AnalyticsPage.tsx**

```tsx
import { useEffect, useState } from "react";
import { api } from "../api";
import type { TeamAnalytics } from "../types";

export default function AnalyticsPage() {
  const [analytics, setAnalytics] = useState<TeamAnalytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.analytics
      .team()
      .then(setAnalytics)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-gray-500">Loading analytics...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;
  if (!analytics) return <div className="text-gray-500">No data available.</div>;

  const approvalRate =
    analytics.totalPullRequests > 0
      ? Math.round((analytics.approvedPullRequests / analytics.totalPullRequests) * 100)
      : 0;

  return (
    <div>
      <h2 className="text-xl font-semibold mb-4">Team Analytics</h2>

      <div className="grid grid-cols-4 gap-4 mb-6">
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{analytics.sessionsThisWeek}</div>
          <div className="text-sm text-gray-500">Sessions This Week</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{approvalRate}%</div>
          <div className="text-sm text-gray-500">PR Approval Rate</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{analytics.pendingPullRequests}</div>
          <div className="text-sm text-gray-500">Pending PRs</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-red-600">{analytics.violationsThisWeek}</div>
          <div className="text-sm text-gray-500">Violations This Week</div>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-4 mb-6">
        <div className="bg-white rounded-lg border p-4">
          <div className="text-lg font-bold">{analytics.totalSessions}</div>
          <div className="text-sm text-gray-500">Total Sessions</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-lg font-bold">{analytics.totalPullRequests}</div>
          <div className="text-sm text-gray-500">Total Pull Requests</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-lg font-bold">{analytics.totalViolations}</div>
          <div className="text-sm text-gray-500">Total Violations</div>
        </div>
      </div>

      <div className="bg-white rounded-lg border overflow-hidden">
        <h3 className="font-semibold px-4 py-3 border-b bg-gray-50">Developer Breakdown</h3>
        {analytics.developers.length === 0 ? (
          <div className="text-gray-500 text-sm text-center py-8">No developer data.</div>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Developer</th>
                <th className="text-right px-4 py-3 font-medium">Sessions</th>
                <th className="text-right px-4 py-3 font-medium">Commits</th>
                <th className="text-right px-4 py-3 font-medium">Violations</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {analytics.developers.map((dev) => (
                <tr key={dev.email} className="hover:bg-gray-50">
                  <td className="px-4 py-3">{dev.email}</td>
                  <td className="px-4 py-3 text-right">{dev.sessionCount}</td>
                  <td className="px-4 py-3 text-right">{dev.commitCount}</td>
                  <td className="px-4 py-3 text-right">
                    <span className={dev.violationCount > 0 ? "text-red-600 font-medium" : ""}>
                      {dev.violationCount}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify typecheck and build**

```bash
cd dashboard && npm run typecheck && npm run build
```

Expected: 0 errors, clean build.

- [ ] **Step 3: Commit**

```bash
git add dashboard/src/pages/AnalyticsPage.tsx
git commit -m "feat: implement Team Analytics page with summary cards and developer table"
```

---

### Task 8: Docker Integration

**Files:**
- Create: `dashboard/nginx.conf`
- Create: `dashboard/Dockerfile`
- Modify: `docker-compose.yml`

- [ ] **Step 1: Create nginx.conf**

```nginx
server {
    listen 80;
    server_name localhost;
    root /usr/share/nginx/html;
    index index.html;

    location /api/ {
        proxy_pass http://overwatch-api:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

- [ ] **Step 2: Create Dockerfile**

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

- [ ] **Step 3: Update docker-compose.yml**

Add the dashboard service after `overwatch-api`:

```yaml
services:
  overwatch-api:
    build:
      context: .
      dockerfile: src/API/DeveloperOverwatch.API/Dockerfile
    ports:
      - "5000:5000"
    environment:
      - Database__ConnectionString=Data Source=/data/overwatch.db
    volumes:
      - overwatch-data:/data

  dashboard:
    build:
      context: ./dashboard
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    depends_on:
      - overwatch-api

  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"
      - "4317:4317"

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"

volumes:
  overwatch-data:
```

- [ ] **Step 4: Verify final build**

```bash
cd dashboard && npm run typecheck && npm run build
```

Expected: 0 errors, `dist/` contains `index.html` + JS/CSS assets.

- [ ] **Step 5: Commit**

```bash
git add dashboard/nginx.conf dashboard/Dockerfile docker-compose.yml
git commit -m "feat: add Docker multi-stage build with nginx for dashboard SPA"
```
