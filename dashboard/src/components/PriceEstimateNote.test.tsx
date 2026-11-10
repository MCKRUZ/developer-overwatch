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

  it("should use the as-of date the caller already has, without fetching the price list again", () => {
    render(<PriceEstimateNote asOf="2026-11-01" />);

    expect(screen.getByText("API-equivalent estimate (prices as of 2026-11-01)")).toBeInTheDocument();
    expect(api.pricing.get).not.toHaveBeenCalled();
  });

  it("falls back to the undated label when pricing cannot be fetched", async () => {
    vi.mocked(api.pricing.get).mockRejectedValue(new Error("offline"));

    render(<PriceEstimateNote />);

    expect(await screen.findByText("API-equivalent estimate")).toBeInTheDocument();
    expect(screen.queryByText(/prices as of/)).not.toBeInTheDocument();
  });

  it("retries on a later mount after a failed fetch instead of caching the failure", async () => {
    vi.mocked(api.pricing.get)
      .mockRejectedValueOnce(new Error("401 before token ready"))
      .mockResolvedValueOnce({ asOf: "2026-11-09" });

    const first = render(<PriceEstimateNote />);
    expect(await screen.findByText("API-equivalent estimate")).toBeInTheDocument();
    first.unmount();

    render(<PriceEstimateNote />);

    expect(
      await screen.findByText("API-equivalent estimate (prices as of 2026-11-09)"),
    ).toBeInTheDocument();
    expect(api.pricing.get).toHaveBeenCalledTimes(2);
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
