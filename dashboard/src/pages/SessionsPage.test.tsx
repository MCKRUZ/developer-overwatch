import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within, userEvent } from "../test/render";
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
    const tbody = document.querySelector("tbody") as HTMLElement;
    expect(within(tbody).getByText("alice@test.com")).toBeInTheDocument();
    expect(within(tbody).getByText("org/app")).toBeInTheDocument();
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
      const tbody = document.querySelector("tbody") as HTMLElement;
      expect(within(tbody).getByText("alice@test.com")).toBeInTheDocument();
    });
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "alice@test.com");
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
      buildSession({ id: "sess-norepo-00000000", repoPath: undefined }),
    ]);
    render(<SessionsPage />);
    await waitFor(() => {
      expect(screen.getByText("—")).toBeInTheDocument();
    });
  });
});
