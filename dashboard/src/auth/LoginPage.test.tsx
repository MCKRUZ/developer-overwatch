import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";

const state = vi.hoisted(() => ({ login: vi.fn() }));

vi.mock("./AuthProvider", () => ({
  useAuthContext: () => ({ login: state.login }),
}));

import LoginPage from "./LoginPage";

describe("LoginPage", () => {
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
});
