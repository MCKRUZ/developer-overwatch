import { describe, it, expect } from "vitest";
import { render, screen } from "../test/render";
import ChartCard from "./ChartCard";

describe("ChartCard", () => {
  it("renders title and children", () => {
    render(
      <ChartCard title="Test Chart">
        <div>Chart content here</div>
      </ChartCard>,
    );
    expect(screen.getByText("Test Chart")).toBeInTheDocument();
    expect(screen.getByText("Chart content here")).toBeInTheDocument();
  });

  it("applies card styling", () => {
    const { container } = render(
      <ChartCard title="Styled">
        <span>inner</span>
      </ChartCard>,
    );
    const card = container.firstElementChild as HTMLElement;
    expect(card.className).toContain("bg-white");
    expect(card.className).toContain("rounded-lg");
    expect(card.className).toContain("border");
  });
});
