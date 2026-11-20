import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";

const state = vi.hoisted(() => ({
  login: vi.fn(),
  loginError: null as string | null,
}));

vi.mock("./AuthProvider", () => ({
  useAuthContext: () => ({ login: state.login, loginError: state.loginError }),
}));

import LoginPage from "./LoginPage";

describe("LoginPage", () => {
  beforeEach(() => {
    state.login = vi.fn();
    state.loginError = null;
  });

  it("renders the sign-in prompt", () => {
    render(<LoginPage />);
    expect(screen.getByText("Developer Overwatch")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });

  it("calls login when the button is clicked", async () => {
    render(<LoginPage />);
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(state.login).toHaveBeenCalledTimes(1);
  });

  it("shows no alert when sign-in has not failed", () => {
    render(<LoginPage />);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows the sign-in error as an alert so a blocked sign-in is not a dead button", () => {
    state.loginError = "Couldn't reach the sign-in server (login.example).";
    render(<LoginPage />);
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Couldn't reach the sign-in server (login.example).",
    );
  });
});
