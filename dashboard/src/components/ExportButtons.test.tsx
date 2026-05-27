import { describe, it, expect, vi } from "vitest";
import { render, screen, userEvent } from "../test/render";
import ExportButtons from "./ExportButtons";

describe("ExportButtons", () => {
  it("should render CSV and PDF buttons", () => {
    render(<ExportButtons onExportCsv={vi.fn()} onExportPdf={vi.fn()} />);

    expect(screen.getByText("Export CSV")).toBeInTheDocument();
    expect(screen.getByText("Export PDF")).toBeInTheDocument();
  });

  it("should call onExportCsv when CSV button is clicked", async () => {
    const onCsv = vi.fn();
    render(<ExportButtons onExportCsv={onCsv} onExportPdf={vi.fn()} />);

    const user = userEvent.setup();
    await user.click(screen.getByText("Export CSV"));

    expect(onCsv).toHaveBeenCalledOnce();
  });

  it("should call onExportPdf when PDF button is clicked", async () => {
    const onPdf = vi.fn();
    render(<ExportButtons onExportCsv={vi.fn()} onExportPdf={onPdf} />);

    const user = userEvent.setup();
    await user.click(screen.getByText("Export PDF"));

    expect(onPdf).toHaveBeenCalledOnce();
  });

  it("should have gray outline styling", () => {
    render(<ExportButtons onExportCsv={vi.fn()} onExportPdf={vi.fn()} />);

    const csvBtn = screen.getByText("Export CSV");
    expect(csvBtn.className).toContain("border-gray-300");
    expect(csvBtn.className).toContain("text-gray-600");
  });
});
