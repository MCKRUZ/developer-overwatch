import { describe, it, expect } from "vitest";
import { render, screen } from "../test/render";
import StatusBadge from "./StatusBadge";

describe("StatusBadge", () => {
  it("renders Pending with yellow styling", () => {
    render(<StatusBadge status="Pending" />);
    const badge = screen.getByText("Pending");
    expect(badge).toHaveClass("bg-yellow-100", "text-yellow-800");
  });

  it("renders Approved with green styling", () => {
    render(<StatusBadge status="Approved" />);
    const badge = screen.getByText("Approved");
    expect(badge).toHaveClass("bg-green-100", "text-green-800");
  });

  it('renders NeedsChanges as "Needs Changes" with red styling', () => {
    render(<StatusBadge status="NeedsChanges" />);
    const badge = screen.getByText("Needs Changes");
    expect(badge).toHaveClass("bg-red-100", "text-red-800");
  });

  it("renders Active with blue styling", () => {
    render(<StatusBadge status="Active" />);
    const badge = screen.getByText("Active");
    expect(badge).toHaveClass("bg-blue-100", "text-blue-800");
  });

  it("renders Completed with gray styling", () => {
    render(<StatusBadge status="Completed" />);
    const badge = screen.getByText("Completed");
    expect(badge).toHaveClass("bg-gray-100", "text-gray-800");
  });

  it("renders Failed with red styling", () => {
    render(<StatusBadge status="Failed" />);
    const badge = screen.getByText("Failed");
    expect(badge).toHaveClass("bg-red-100", "text-red-800");
  });

  it("renders unknown status with gray fallback", () => {
    render(<StatusBadge status="SomethingElse" />);
    const badge = screen.getByText("SomethingElse");
    expect(badge).toHaveClass("bg-gray-100", "text-gray-800");
  });
});
