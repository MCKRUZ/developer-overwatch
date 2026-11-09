import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "../test/render";
import { api } from "../api";
import PriceEstimateNote, { resetPriceEstimateCache } from "./PriceEstimateNote";

vi.mock("../api", () => ({
  api: {
    pricing: {
      get: vi.fn(),
    },
  },
}));

describe("PriceEstimateNote", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetPriceEstimateCache();
  });

  it("labels dollar figures with the price-table date", async () => {
    vi.mocked(api.pricing.get).mockResolvedValue({ asOf: "2026-11-09" });

    render(<PriceEstimateNote />);

    expect(
      await screen.findByText("API-equivalent estimate (prices as of 2026-11-09)"),
    ).toBeInTheDocument();
  });

  it("falls back to the undated label when pricing cannot be fetched", async () => {
    vi.mocked(api.pricing.get).mockRejectedValue(new Error("offline"));

    render(<PriceEstimateNote />);

    expect(await screen.findByText("API-equivalent estimate")).toBeInTheDocument();
    expect(screen.queryByText(/prices as of/)).not.toBeInTheDocument();
  });

  it("fetches pricing once across multiple mounts", async () => {
    vi.mocked(api.pricing.get).mockResolvedValue({ asOf: "2026-11-09" });

    render(
      <>
        <PriceEstimateNote />
        <PriceEstimateNote />
      </>,
    );

    expect(
      await screen.findAllByText("API-equivalent estimate (prices as of 2026-11-09)"),
    ).toHaveLength(2);
    expect(api.pricing.get).toHaveBeenCalledTimes(1);
  });

  it("applies the caller's className", async () => {
    vi.mocked(api.pricing.get).mockResolvedValue({ asOf: "2026-11-09" });

    render(<PriceEstimateNote className="mt-1" />);

    const note = await screen.findByText(/prices as of/);
    expect(note).toHaveClass("mt-1");
  });
});
