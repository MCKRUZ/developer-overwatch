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
    const links = screen.getAllByRole("link", { name: "PR Queue" });
    expect(links[0]).toHaveAttribute("href", "/");
  });

  it("renders Sessions link pointing to /sessions", () => {
    render(<Layout />);
    const links = screen.getAllByRole("link", { name: "Sessions" });
    expect(links[0]).toHaveAttribute("href", "/sessions");
  });

  it("renders Analytics link pointing to /analytics", () => {
    render(<Layout />);
    const links = screen.getAllByRole("link", { name: "Analytics" });
    expect(links[0]).toHaveAttribute("href", "/analytics");
  });
});
