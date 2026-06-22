import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, beforeEach, vi } from "vitest";

const state = vi.hoisted(() => ({
  configured: false,
  token: null as string | null,
}));

const api = vi.hoisted(() => ({
  setTokenGetter: vi.fn(),
  clearTokenGetter: vi.fn(),
}));

vi.mock("./api", () => api);

vi.mock("./auth/AuthProvider", () => ({
  default: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  get isAuthConfigured() {
    return state.configured;
  },
  useAuthContext: () => ({ token: state.token }),
}));

vi.mock("./auth/ProtectedRoute", () => ({
  default: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock("./components/Layout", () => ({
  default: () => <div>layout</div>,
}));
vi.mock("./pages/PrQueuePage", () => ({ default: () => <div>pr</div> }));
vi.mock("./pages/SessionsPage", () => ({ default: () => <div>sessions</div> }));
vi.mock("./pages/SessionDetailPage", () => ({ default: () => <div>detail</div> }));
vi.mock("./pages/AnalyticsPage", () => ({ default: () => <div>analytics</div> }));
vi.mock("./pages/GovernancePage", () => ({ default: () => <div>governance</div> }));

import App from "./App";

describe("App", () => {
  beforeEach(() => {
    state.configured = false;
    state.token = null;
    api.setTokenGetter.mockClear();
    api.clearTokenGetter.mockClear();
  });

  it("renders routed content without syncing tokens when auth is unconfigured", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByText("layout")).toBeInTheDocument();
    expect(api.setTokenGetter).not.toHaveBeenCalled();
  });

  it("registers a token getter when configured and a token is present", () => {
    state.configured = true;
    state.token = "tok-xyz";

    render(
      <MemoryRouter initialEntries={["/"]}>
        <App />
      </MemoryRouter>,
    );

    expect(api.setTokenGetter).toHaveBeenCalledTimes(1);
    const getter = api.setTokenGetter.mock.calls[0][0] as () => string;
    expect(getter()).toBe("tok-xyz");
  });

  it("clears the token getter when configured but no token is present", () => {
    state.configured = true;
    state.token = null;

    render(
      <MemoryRouter initialEntries={["/"]}>
        <App />
      </MemoryRouter>,
    );

    expect(api.clearTokenGetter).toHaveBeenCalled();
  });
});
