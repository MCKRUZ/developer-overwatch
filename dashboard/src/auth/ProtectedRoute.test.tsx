import { render, screen } from "@testing-library/react";
import { describe, it, expect, beforeEach, vi } from "vitest";

const state = vi.hoisted(() => ({
  configured: false,
  auth: { isAuthenticated: false, isLoading: false },
}));

vi.mock("./AuthProvider", () => ({
  get isAuthConfigured() {
    return state.configured;
  },
  useAuthContext: () => state.auth,
}));

import ProtectedRoute from "./ProtectedRoute";

describe("ProtectedRoute", () => {
  beforeEach(() => {
    state.configured = false;
    state.auth = { isAuthenticated: false, isLoading: false };
  });

  it("renders children when auth is not configured", () => {
    render(
      <ProtectedRoute>
        <div>secret</div>
      </ProtectedRoute>,
    );
    expect(screen.getByText("secret")).toBeInTheDocument();
  });

  it("shows a spinner while loading", () => {
    state.configured = true;
    state.auth = { isAuthenticated: false, isLoading: true };
    const { container } = render(
      <ProtectedRoute>
        <div>secret</div>
      </ProtectedRoute>,
    );
    expect(screen.queryByText("secret")).not.toBeInTheDocument();
    expect(container.querySelector(".animate-spin")).toBeInTheDocument();
  });

  it("shows the login page when configured but unauthenticated", () => {
    state.configured = true;
    state.auth = { isAuthenticated: false, isLoading: false };
    render(
      <ProtectedRoute>
        <div>secret</div>
      </ProtectedRoute>,
    );
    expect(screen.queryByText("secret")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });

  it("renders children when configured and authenticated", () => {
    state.configured = true;
    state.auth = { isAuthenticated: true, isLoading: false };
    render(
      <ProtectedRoute>
        <div>secret</div>
      </ProtectedRoute>,
    );
    expect(screen.getByText("secret")).toBeInTheDocument();
  });
});
