import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { exportJson } from "./exportJson";

describe("exportJson", () => {
  let anchorEl: Partial<HTMLAnchorElement>;
  let blobParts: BlobPart[] = [];
  let blobType = "";

  beforeEach(() => {
    anchorEl = { click: vi.fn(), style: {} as CSSStyleDeclaration } as Partial<HTMLAnchorElement>;
    vi.spyOn(document, "createElement").mockReturnValue(anchorEl as HTMLAnchorElement);
    vi.spyOn(document.body, "appendChild").mockReturnValue(null as unknown as Node);
    vi.spyOn(document.body, "removeChild").mockReturnValue(null as unknown as Node);
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:json");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    const OrigBlob = globalThis.Blob;
    vi.stubGlobal(
      "Blob",
      class CapturingBlob extends OrigBlob {
        constructor(parts?: BlobPart[], options?: BlobPropertyBag) {
          super(parts, options);
          blobParts = parts ?? [];
          blobType = options?.type ?? "";
        }
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("should download the data as JSON with raw values, so numbers stay numbers in a finance tool", () => {
    exportJson("usage", { pricesAsOf: "2026-11-01", rows: [{ cost: 1.5, cacheHitRate: null }] });

    expect(anchorEl.download).toBe("usage.json");
    expect(blobType).toBe("application/json;charset=utf-8;");
    expect(JSON.parse(String(blobParts[0]))).toEqual({
      pricesAsOf: "2026-11-01",
      rows: [{ cost: 1.5, cacheHitRate: null }],
    });
    expect(anchorEl.click).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:json");
  });

  it("should not double-append .json when the name already has it", () => {
    exportJson("usage.json", []);
    expect(anchorEl.download).toBe("usage.json");
  });
});
