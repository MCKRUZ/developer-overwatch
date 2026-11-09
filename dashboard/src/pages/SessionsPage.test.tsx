import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within, userEvent } from "../test/render";
import SessionsPage from "./SessionsPage";
import { api } from "../api";
import { buildSessionListItem } from "../test/factories";

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
      buildSessionListItem({ id: "abcdef12-3456-7890-abcd-ef1234567890", developerEmail: "alice@test.com", repoPath: "org/app" }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("abcdef12...")).toBeInTheDocument();
    });
    const link = screen.getByRole("link", { name: "abcdef12..." });
    expect(link).toHaveAttribute("href", "/sessions/abcdef12-3456-7890-abcd-ef1234567890");
    const tbody = document.querySelector("tbody") as HTMLElement;
    expect(within(tbody).getByText("alice@test.com")).toBeInTheDocument();
    expect(within(tbody).getByText("app")).toBeInTheDocument();
  });

  it("shows 'In progress' for active sessions without endedAt", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({ id: "sess-active-00000000", status: "Active", endedAt: undefined }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("In progress")).toBeInTheDocument();
    });
  });

  it("shows formatted duration for completed sessions", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({
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
    mockApi.sessions.list.mockResolvedValueOnce([
      buildSessionListItem({ id: "sess-a-0000000000000", developerEmail: "alice@test.com" }),
      buildSessionListItem({ id: "sess-b-0000000000000", developerEmail: "bob@test.com" }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      const tbody = document.querySelector("tbody") as HTMLElement;
      expect(within(tbody).getByText("alice@test.com")).toBeInTheDocument();
    });

    mockApi.sessions.list.mockResolvedValueOnce([
      buildSessionListItem({ id: "sess-a-0000000000000", developerEmail: "alice@test.com" }),
    ]);
    const select = screen.getByRole("combobox", { name: "Developer" });
    await user.selectOptions(select, "alice@test.com");

    await waitFor(() => {
      expect(mockApi.sessions.list).toHaveBeenLastCalledWith(
        expect.objectContaining({ developer: "alice@test.com" }),
      );
    });
    const tbody = document.querySelector("tbody") as HTMLElement;
    expect(within(tbody).getByText("alice@test.com")).toBeInTheDocument();
    expect(within(tbody).queryByText("bob@test.com")).not.toBeInTheDocument();
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
      buildSessionListItem({ id: "sess-norepo-00000000", repoPath: undefined }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getAllByText("—")).toHaveLength(2); // User and Repo columns
    });
  });

  it("applies from/to/developer filters carried in the URL from an analytics drill-down", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({ id: "sess-drill-00000000", developerEmail: "alice@test.com" }),
    ]);
    render(<SessionsPage />, {
      initialEntries: [
        "/sessions?from=2026-05-27T00:00:00.000Z&to=2026-05-28T00:00:00.000Z&developer=alice%40test.com",
      ],
    });

    await waitFor(() => {
      expect(mockApi.sessions.list).toHaveBeenCalledWith({
        developer: "alice@test.com",
        repo: undefined,
        status: undefined,
        from: "2026-05-27T00:00:00.000Z",
        to: "2026-05-28T00:00:00.000Z",
      });
    });
    const banner = screen.getByText(/Drilled into/).closest("div") as HTMLElement;
    expect(within(banner).getByText("alice@test.com")).toBeInTheDocument();
  });

  it("clears the drill-down filter and reverts to today", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([]);
    render(<SessionsPage />, {
      initialEntries: [
        "/sessions?from=2026-05-27T00:00:00.000Z&to=2026-05-28T00:00:00.000Z&developer=alice%40test.com",
      ],
    });

    await waitFor(() => {
      expect(screen.getByText(/Drilled into/)).toBeInTheDocument();
    });

    await user.click(screen.getByText("Clear"));

    expect(screen.queryByText(/Drilled into/)).not.toBeInTheDocument();
    await waitFor(() => {
      expect(mockApi.sessions.list).toHaveBeenLastCalledWith(
        expect.objectContaining({ developer: undefined }),
      );
    });
  });

  it("pins an active session above the day groups even if it started days ago", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({
        id: "resumed-old-session0",
        status: "Active",
        startedAt: "2026-05-20T08:00:00Z",
      }),
      buildSessionListItem({
        id: "today-session-0000000",
        status: "Completed",
        startedAt: "2026-05-27T08:00:00Z",
      }),
    ]);
    render(<SessionsPage />);

    await waitFor(() => {
      expect(screen.getByText("Active Now")).toBeInTheDocument();
    });
    const activeSection = screen.getByText("Active Now").closest("div")!.parentElement as HTMLElement;
    expect(within(activeSection).getByText("resumed-...")).toBeInTheDocument();
  });

  it("shows the session's custom title instead of the raw GUID when available", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({ id: "abcdef12-3456-7890-abcd-ef1234567890", title: "initial-setup" }),
    ]);
    render(<SessionsPage />);

    await waitFor(() => {
      expect(screen.getByText("initial-setup")).toBeInTheDocument();
    });
    expect(screen.queryByText("abcdef12...")).not.toBeInTheDocument();
  });

  it("shows just the repo name and the OS user extracted from the path, not the full path", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({
        id: "sess-path-000000000",
        repoPath: "C:/Users/jane.doe/source/repos/overwatch",
      }),
    ]);
    render(<SessionsPage />);

    await waitFor(() => {
      expect(document.querySelector("tbody")).toBeInTheDocument();
    });
    const tbody = document.querySelector("tbody") as HTMLElement;
    expect(within(tbody).getByText("overwatch")).toBeInTheDocument();
    expect(within(tbody).getByText("jane.doe")).toBeInTheDocument();
    expect(screen.queryByText("C:/Users/jane.doe/source/repos/overwatch")).not.toBeInTheDocument();
  });

  it("shows the day's own usage in the Daily Tokens/Cost columns, not the session's lifetime total", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSessionListItem({
        id: "sess-daily-00000000",
        startedAt: "2026-05-27T08:00:00Z",
        totalInputTokens: 9999,
        totalOutputTokens: 9999,
        estimatedCostUsd: 999.99,
        dailyUsage: {
          "2026-05-27": {
            inputTokens: 100,
            outputTokens: 50,
            cacheCreationInputTokens: 850,
            cacheReadInputTokens: 4000,
            estimatedCostUsd: 1.23,
          },
        },
      }),
    ]);
    render(<SessionsPage />);

    // Cache tokens are part of the day's total, the same way the detail page counts them.
    await waitFor(() => {
      expect(screen.getByText("5,000")).toBeInTheDocument();
    });
    expect(screen.getByText("$1.23")).toBeInTheDocument();
    expect(screen.queryByText("9,999")).not.toBeInTheDocument();
    expect(screen.queryByText("$999.99")).not.toBeInTheDocument();
  });

  it("paginates the Active Now section 10 at a time", async () => {
    const user = userEvent.setup();
    const activeSessions = Array.from({ length: 15 }, (_, i) =>
      buildSessionListItem({
        id: `active-${String(i).padStart(2, "0")}-000000`,
        status: "Active",
        startedAt: `2026-05-27T08:${String(i).padStart(2, "0")}:00Z`,
      }),
    );
    mockApi.sessions.list.mockResolvedValue(activeSessions);
    render(<SessionsPage />);

    await waitFor(() => {
      expect(screen.getByText("15 sessions")).toBeInTheDocument();
    });
    expect(screen.getByText("Page 1 of 2")).toBeInTheDocument();
    const activeSection = screen.getByText("Active Now").closest("div")!.parentElement as HTMLElement;
    expect(within(activeSection).getAllByRole("link", { name: /\.\.\.$/ })).toHaveLength(10);

    await user.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByText("Page 2 of 2")).toBeInTheDocument();
    expect(within(activeSection).getAllByRole("link", { name: /\.\.\.$/ })).toHaveLength(5);
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
  });
});
