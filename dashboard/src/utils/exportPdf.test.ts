import { describe, it, expect, vi, beforeEach } from "vitest";

const mockText = vi.fn();
const mockSetFontSize = vi.fn();
const mockSetTextColor = vi.fn();
const mockSave = vi.fn();
const mockAutoTable = vi.fn();

vi.mock("jspdf", () => {
  return {
    jsPDF: class MockJsPDF {
      text = mockText;
      setFontSize = mockSetFontSize;
      setTextColor = mockSetTextColor;
      save = mockSave;
    },
  };
});

vi.mock("jspdf-autotable", () => ({
  default: (...args: unknown[]) => mockAutoTable(...args),
}));

import { exportPdf } from "./exportPdf";

describe("exportPdf", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("should create a PDF with title and timestamp", () => {
    exportPdf("report", "Test Report", ["Col1"], [["val1"]]);

    expect(mockSetFontSize).toHaveBeenCalledWith(16);
    expect(mockText).toHaveBeenCalledWith("Test Report", 14, 20);
    expect(mockText).toHaveBeenCalledWith(expect.stringContaining("Generated on:"), 14, 28);
  });

  it("should call autoTable with headers and rows", () => {
    const headers = ["Name", "Value"];
    const rows = [["Alice", "42"]];

    exportPdf("report", "Title", headers, rows);

    expect(mockAutoTable).toHaveBeenCalledOnce();
    const args = mockAutoTable.mock.calls[0];
    expect(args[1]).toMatchObject({
      head: [headers],
      body: rows,
    });
  });

  it("should save with .pdf extension", () => {
    exportPdf("report", "Title", [], []);
    expect(mockSave).toHaveBeenCalledWith("report.pdf");
  });

  it("should not double-append .pdf if filename already has it", () => {
    exportPdf("report.pdf", "Title", [], []);
    expect(mockSave).toHaveBeenCalledWith("report.pdf");
  });
});
