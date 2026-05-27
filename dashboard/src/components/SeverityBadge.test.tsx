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
