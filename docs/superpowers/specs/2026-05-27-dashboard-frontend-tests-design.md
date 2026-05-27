# Dashboard Frontend Tests Design

**Date:** 2026-05-27
**Scope:** Add test suite to the React dashboard (currently 0% coverage, target 80%+)
**Codebase:** 931 lines across 11 source files (4 pages, 3 components, 1 API client, 1 types file, 1 entry point, 1 env declaration)

## Stack

| Tool | Purpose |
|------|---------|
| vitest | Test runner (native Vite integration) |
| @vitest/coverage-v8 | Coverage reporting with threshold enforcement |
| @testing-library/react | Component rendering and queries |
| @testing-library/jest-dom | DOM matchers (toBeInTheDocument, toHaveClass, etc.) |
| @testing-library/user-event | User interaction simulation (click, type, select) |
| jsdom | Browser environment for Vitest |

## Configuration

### vitest.config.ts
- Extends `vite.config.ts` to inherit aliases and plugins
- Environment: `jsdom`
- Setup file: `src/test/setup.ts`
- Coverage provider: `v8`
- Coverage thresholds: 80% lines, 80% branches, 80% functions
- Include: `src/**/*.{ts,tsx}`, exclude: `src/main.tsx`, `src/vite-env.d.ts`

### package.json scripts
- `test` — run all tests once
- `test:watch` — watch mode
- `test:coverage` — run with coverage report and threshold enforcement

### src/test/setup.ts
- Imports `@testing-library/jest-dom/vitest` for extended matchers

## Test Utilities

### src/test/render.tsx — Custom render wrapper
- Wraps components in `MemoryRouter` with configurable `initialEntries`
- Re-exports all `@testing-library/react` exports
- Pages using `useParams`/`useNavigate` get proper router context

### src/test/factories.ts — Type-safe mock data factories
Each factory returns a complete valid object. Overrides are `Partial<T>` spread on top.

- `buildSession(overrides?)` -> `Session`
- `buildConversationTurn(overrides?)` -> `ConversationTurn`
- `buildToolExecution(overrides?)` -> `ToolExecution`
- `buildFileChange(overrides?)` -> `FileChange`
- `buildGovernanceViolation(overrides?)` -> `GovernanceViolation`
- `buildArchitectAnnotation(overrides?)` -> `ArchitectAnnotation`
- `buildPullRequestReview(overrides?)` -> `PullRequestReview`
- `buildPullRequestDetail(overrides?)` -> `PullRequestDetail`
- `buildTeamAnalytics(overrides?)` -> `TeamAnalytics`
- `buildDeveloperAnalytics(overrides?)` -> `DeveloperAnalytics`
- `buildDeveloperMetrics(overrides?)` -> `DeveloperMetrics`

### API mock pattern
Each page test file uses `vi.mock('../api')` at the module level:
```typescript
vi.mock('../api');
const mockApi = vi.mocked(api);

beforeEach(() => {
  mockApi.sessions.list.mockResolvedValue([buildSession()]);
});
```

## Test Plan

### Layer 1: API Client

**`src/api.test.ts`** (~15 tests)

Mock `globalThis.fetch` to verify the API client constructs correct requests.

| Test | What it verifies |
|------|-----------------|
| sessions.list() — no params | GET /api/sessions |
| sessions.list(developer, repo) | GET /api/sessions?developer=x&repo=y |
| sessions.get(id) | GET /api/sessions/{id} |
| pullRequests.list() — no params | GET /api/prs |
| pullRequests.list(repo) | GET /api/prs?repo=x |
| pullRequests.get(owner, repo, pr) | GET /api/prs/{owner}/{repo}/{pr} |
| pullRequests.review() | POST /api/prs/{owner}/{repo}/{pr}/review with JSON body |
| analytics.team() | GET /api/analytics/team |
| analytics.developer(email) | GET /api/analytics/developer/{email} |
| governance.violations(sessionId) | GET /api/governance/violations/{sessionId} |
| governance.violations(sessionId, severity) | GET /api/governance/violations/{sessionId}?severity=x |
| governance.resolve(id, body) | POST /api/governance/violations/{id}/resolve with JSON body |
| annotations.create(body) | POST /api/annotations with JSON body |
| Error: non-ok response | Throws error with status text |
| Error: network failure | Throws/propagates fetch error |

### Layer 2: Reusable Components

**`src/components/StatusBadge.test.tsx`** (~6 tests)

| Test | What it verifies |
|------|-----------------|
| Renders "Pending" with yellow styling | status="Pending" -> yellow classes |
| Renders "Approved" with green styling | status="Approved" -> green classes |
| Renders "NeedsChanges" as "Needs Changes" | Label transformation + red classes |
| Renders "Active" with blue styling | status="Active" -> blue classes |
| Renders "Completed" with green styling | status="Completed" -> green classes |
| Renders "Failed" with red styling | status="Failed" -> red classes |

**`src/components/SeverityBadge.test.tsx`** (~5 tests)

| Test | What it verifies |
|------|-----------------|
| Renders "Critical" with dark red styling | severity="Critical" -> red-600 bg |
| Renders "Error" with light red styling | severity="Error" -> red-100 bg |
| Renders "Warning" with yellow styling | severity="Warning" -> yellow-100 bg |
| Renders "Info" with blue styling | severity="Info" -> blue-100 bg |
| Unknown severity gets gray fallback | severity="Unknown" -> gray classes |

**`src/components/Layout.test.tsx`** (~4 tests)

| Test | What it verifies |
|------|-----------------|
| Renders navigation with 3 links | PR Queue, Sessions, Analytics present |
| PR Queue links to "/" | href="/" |
| Sessions links to "/sessions" | href="/sessions" |
| Analytics links to "/analytics" | href="/analytics" |

### Layer 3: Pages

**`src/pages/PrQueuePage.test.tsx`** (~10 tests)

| Test | What it verifies |
|------|-----------------|
| Shows loading state | Loading indicator while fetch pending |
| Renders metric cards | Total, Pending, Approved, Needs Changes counts |
| Renders PR table rows | Row per PR with correct data |
| Sort by PR number | Click header toggles asc/desc |
| Sort by repository | Click header toggles asc/desc |
| Sort by status | Click header toggles asc/desc |
| Filter by status | Dropdown filters table rows |
| Error state | Shows error message on fetch failure |
| Empty state | No PRs message when list empty |
| Calls api.pullRequests.list() on mount | Fetch triggered once |

**`src/pages/SessionsPage.test.tsx`** (~8 tests)

| Test | What it verifies |
|------|-----------------|
| Shows loading state | Loading indicator while fetch pending |
| Renders session table rows | Row per session with links |
| Session ID links to detail page | href="/sessions/{id}" |
| Filter by developer email | Input filters visible rows |
| Duration formatting — completed | Shows calculated duration string |
| Duration formatting — ongoing | Shows "Ongoing" for active sessions |
| Error state | Shows error message on fetch failure |
| Empty state | No sessions message when list empty |

**`src/pages/SessionDetailPage.test.tsx`** (~14 tests)

| Test | What it verifies |
|------|-----------------|
| Shows loading state | Loading indicator while fetch pending |
| Renders session header metadata | Repo, started, ended, turn count |
| Renders governance violations | Violation cards with SeverityBadge |
| Shows resolved state on violations | Visual distinction for resolved |
| Renders conversation timeline | Turns with role-based styling |
| Highlights decision turns | Decision marker visible |
| Shows token counts on turns | Token count displayed |
| Renders file changes | File paths + diff text |
| Renders existing annotations | Annotation list with metadata |
| Add annotation — fill and submit | Form interaction + API call |
| Add annotation — refetches session | session.get called again after submit |
| Add annotation — shows submitting state | Button disabled during submit |
| Error state | Shows error message on fetch failure |
| Back link to sessions list | href="/sessions" |

**`src/pages/AnalyticsPage.test.tsx`** (~8 tests)

| Test | What it verifies |
|------|-----------------|
| Shows loading state | Loading indicator while fetch pending |
| Renders 7 metric cards | All KPIs with correct values |
| PR approval rate calculation | Percentage derived from approved/total |
| Renders developer breakdown table | Row per developer |
| Violation count red highlight | Red text when violationCount > 0 |
| Normal violation count styling | Default text when violationCount = 0 |
| Error state | Shows error message on fetch failure |
| Empty analytics | Handles zero-data gracefully |

## Coverage Target

| Metric | Target | Estimated |
|--------|--------|-----------|
| Lines | 80% | 85-90% |
| Branches | 80% | 85-90% |
| Functions | 80% | 85-90% |

**~70 tests** across **8 test files** + **3 utility files** (setup, render, factories).

Uncovered code will primarily be: `main.tsx` (entry point, excluded), CSS class string assembly edge paths, and `vite-env.d.ts`.

## File Structure

```
dashboard/
  src/
    test/
      setup.ts
      render.tsx
      factories.ts
    api.test.ts
    components/
      StatusBadge.test.tsx
      SeverityBadge.test.tsx
      Layout.test.tsx
    pages/
      PrQueuePage.test.tsx
      SessionsPage.test.tsx
      SessionDetailPage.test.tsx
      AnalyticsPage.test.tsx
  vitest.config.ts
```

## Implementation Order

1. Install dependencies and create config files
2. Create test utilities (setup, render, factories)
3. API client tests
4. Component tests (StatusBadge, SeverityBadge, Layout)
5. Page tests (PrQueuePage, SessionsPage, SessionDetailPage, AnalyticsPage)
6. Run coverage report and fill gaps if below 80%
