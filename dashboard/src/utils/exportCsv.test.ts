import { describe, it, expect, vi, beforeEach } from "vitest";
import { exportCsv } from "./exportCsv";

describe("exportCsv", () => {
  let clickSpy: ReturnType<typeof vi.fn>;
  let anchorEl: Partial<HTMLAnchorElement>;

  beforeEach(() => {
    clickSpy = vi.fn();
    anchorEl = { click: clickSpy, style: {} as CSSStyleDeclaration } as Partial<HTMLAnchorElement>;

    vi.spyOn(document, "createElement").mockReturnValue(anchorEl as HTMLAnchorElement);
    vi.spyOn(document.body, "appendChild").mockReturnValue(null as unknown as Node);
    vi.spyOn(document.body, "removeChild").mockReturnValue(null as unknown as Node);
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:mock-url");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  });

  it("should generate CSV and trigger download", () => {
    exportCsv("report", ["Name", "Value"], [["Alice", "100"]]);

    expect(anchorEl.download).toBe("report.csv");
    expect(anchorEl.href).toBe("blob:mock-url");
    expect(clickSpy).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mock-url");
  });

  it("should not double-append .csv if filename already has it", () => {
    exportCsv("report.csv", ["A"], [["1"]]);
    expect(anchorEl.download).toBe("report.csv");
  });

  it("should escape values containing commas", () => {
    let captured = "";
    const OrigBlob = globalThis.Blob;
    vi.stubGlobal(
      "Blob",
      class MockBlob extends OrigBlob {
        constructor(parts?: BlobPart[], options?: BlobPropertyBag) {
          super(parts, options);
          captured = parts?.[0] as string;
        }
      },
    );

    exportCsv("test", ["Col"], [["hello, world"]]);
    expect(captured).toContain('"hello, world"');

    vi.unstubAllGlobals();
  });

  it("should escape values containing double quotes", () => {
    let captured = "";
    const OrigBlob = globalThis.Blob;
    vi.stubGlobal(
      "Blob",
      class MockBlob extends OrigBlob {
        constructor(parts?: BlobPart[], options?: BlobPropertyBag) {
          super(parts, options);
          captured = parts?.[0] as string;
        }
      },
    );

    exportCsv("test", ["Col"], [['say "hi"']]);
    expect(captured).toContain('"say ""hi"""');

    vi.unstubAllGlobals();
  });

  it("should escape values containing newlines", () => {
    let captured = "";
    const OrigBlob = globalThis.Blob;
    vi.stubGlobal(
      "Blob",
      class MockBlob extends OrigBlob {
        constructor(parts?: BlobPart[], options?: BlobPropertyBag) {
          super(parts, options);
          captured = parts?.[0] as string;
        }
      },
    );

    exportCsv("test", ["Col"], [["line1\nline2"]]);
    expect(captured).toContain('"line1\nline2"');

    vi.unstubAllGlobals();
  });

  it("should handle empty rows", () => {
    let captured = "";
    const OrigBlob = globalThis.Blob;
    vi.stubGlobal(
      "Blob",
      class MockBlob extends OrigBlob {
        constructor(parts?: BlobPart[], options?: BlobPropertyBag) {
          super(parts, options);
          captured = parts?.[0] as string;
        }
      },
    );

    exportCsv("empty", ["A", "B"], []);
    expect(captured).toBe("A,B");

    vi.unstubAllGlobals();
  });
});
