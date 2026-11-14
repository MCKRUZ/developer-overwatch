import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within, userEvent } from "../test/render";
import SettingsPage from "./SettingsPage";
import { api, ApiError } from "../api";
import { buildBudget, buildCurrentUser, buildDeveloperPlan } from "../test/meterFactories";

vi.mock("../api", async (importOriginal) => ({
  ApiError: (await importOriginal<typeof import("../api")>()).ApiError,
  api: {
    auth: { me: vi.fn() },
    pricing: { get: vi.fn().mockResolvedValue({ asOf: "2026-11-01" }) },
    budgets: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
    developerPlans: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
  },
}));

const mockApi = vi.mocked(api, true);
const READ_ONLY = "Editing needs the Lead role. Servers without sign-in only allow editing in local development.";

const validation = (errors: Record<string, string[]>) =>
  new ApiError(400, JSON.stringify({ title: "One or more validation errors occurred.", status: 400, errors }));

function region(name: string) {
  return within(screen.getByRole("region", { name }));
}

async function renderLoaded() {
  render(<SettingsPage />);
  await screen.findByRole("cell", { name: "Team" });
  return userEvent.setup();
}

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockApi.auth.me.mockResolvedValue(buildCurrentUser({ canEditBudgets: true }));
    mockApi.budgets.list.mockResolvedValue([buildBudget()]);
    mockApi.developerPlans.list.mockResolvedValue([buildDeveloperPlan()]);
    mockApi.budgets.create.mockResolvedValue(buildBudget());
    mockApi.budgets.update.mockResolvedValue(buildBudget());
    mockApi.budgets.remove.mockResolvedValue(undefined);
    mockApi.developerPlans.create.mockResolvedValue(buildDeveloperPlan());
    mockApi.developerPlans.update.mockResolvedValue(buildDeveloperPlan());
    mockApi.developerPlans.remove.mockResolvedValue(undefined);
  });

  describe("read-only", () => {
    it.each([
      ["the server says the viewer may not edit", () => mockApi.auth.me.mockResolvedValue(buildCurrentUser({ canEditBudgets: false }))],
      ["the viewer's rights cannot be read", () => mockApi.auth.me.mockRejectedValue(new Error("500: boom"))],
    ])("should show budgets and plans without edit controls when %s", async (_, arrange) => {
      arrange();
      render(<SettingsPage />);

      expect(await screen.findByText(READ_ONLY)).toBeInTheDocument();
      expect(await screen.findByRole("cell", { name: "Team" })).toBeInTheDocument();
      expect(screen.getByText("Max 20x")).toBeInTheDocument();
      expect(screen.queryByRole("button")).not.toBeInTheDocument();
      expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    });

    it("should not show the read-only note to a lead", async () => {
      await renderLoaded();
      expect(screen.queryByText(READ_ONLY)).not.toBeInTheDocument();
    });
  });

  it("should show a load failure", async () => {
    mockApi.budgets.list.mockRejectedValue(new Error("503: busy"));
    render(<SettingsPage />);
    expect(await screen.findByText("Could not load settings: 503: busy")).toBeInTheDocument();
  });

  describe("budgets", () => {
    it("should create a team budget with no developer email, then reload the list", async () => {
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.selectOptions(budgets.getByLabelText("Period"), "Monthly");
      await user.type(budgets.getByLabelText("Amount (USD)"), "900");
      await user.click(budgets.getByRole("button", { name: "Add budget" }));

      expect(mockApi.budgets.create).toHaveBeenCalledWith({
        scope: "Team",
        developerEmail: null,
        period: "Monthly",
        amountUsd: 900,
      });
      await waitFor(() => expect(mockApi.budgets.list).toHaveBeenCalledTimes(2));
    });

    it("should create a developer budget for the trimmed email", async () => {
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.selectOptions(budgets.getByLabelText("Scope"), "Developer");
      await user.type(budgets.getByLabelText("Developer email"), "  bob@x.io ");
      await user.type(budgets.getByLabelText("Amount (USD)"), "25.5");
      await user.click(budgets.getByRole("button", { name: "Add budget" }));

      expect(mockApi.budgets.create).toHaveBeenCalledWith({
        scope: "Developer",
        developerEmail: "bob@x.io",
        period: "Daily",
        amountUsd: 25.5,
      });
    });

    it("should explain a duplicate budget, which the server refuses with 409", async () => {
      mockApi.budgets.create.mockRejectedValue(new ApiError(409, JSON.stringify({ title: "Conflict", status: 409 })));
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.type(budgets.getByLabelText("Amount (USD)"), "60");
      await user.click(budgets.getByRole("button", { name: "Add budget" }));

      expect(await budgets.findByRole("alert")).toHaveTextContent(
        "A Team Daily budget already exists. Edit it instead.",
      );
    });

    it("should show the server's validation messages for a rejected budget", async () => {
      mockApi.budgets.create.mockRejectedValue(
        validation({ AmountUsd: ["Amount must be more than 0."], DeveloperEmail: ["Email is required."] }),
      );
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.type(budgets.getByLabelText("Amount (USD)"), "0");
      await user.click(budgets.getByRole("button", { name: "Add budget" }));

      expect(await budgets.findByRole("alert")).toHaveTextContent("Amount must be more than 0. Email is required.");
    });

    it("should edit only a budget's amount", async () => {
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.click(budgets.getByRole("button", { name: "Edit Team Daily budget" }));
      const amount = budgets.getByLabelText("New amount for Team Daily budget");
      await user.clear(amount);
      await user.type(amount, "75");
      await user.click(budgets.getByRole("button", { name: "Save" }));

      expect(mockApi.budgets.update).toHaveBeenCalledWith("b-team-daily", 75);
      await waitFor(() => expect(budgets.queryByRole("button", { name: "Save" })).not.toBeInTheDocument());
      expect(mockApi.budgets.list).toHaveBeenCalledTimes(2);
    });

    it("should leave the amount alone when an edit is cancelled", async () => {
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.click(budgets.getByRole("button", { name: "Edit Team Daily budget" }));
      await user.click(budgets.getByRole("button", { name: "Cancel" }));

      expect(mockApi.budgets.update).not.toHaveBeenCalled();
      expect(budgets.getByText("$50.00")).toBeInTheDocument();
    });

    it("should start each edit from the current amount, so an abandoned edit or a reload is never saved back", async () => {
      const user = await renderLoaded();
      const budgets = region("Budgets");
      await user.click(budgets.getByRole("button", { name: "Edit Team Daily budget" }));
      await user.type(budgets.getByLabelText("New amount for Team Daily budget"), "999");
      await user.click(budgets.getByRole("button", { name: "Cancel" }));

      await user.click(budgets.getByRole("button", { name: "Edit Team Daily budget" }));

      expect(budgets.getByLabelText("New amount for Team Daily budget")).toHaveValue(50);
    });

    it("should delete a budget only after confirmation", async () => {
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.click(budgets.getByRole("button", { name: "Delete Team Daily budget" }));
      await user.click(budgets.getByRole("button", { name: "Cancel" }));
      expect(mockApi.budgets.remove).not.toHaveBeenCalled();

      await user.click(budgets.getByRole("button", { name: "Delete Team Daily budget" }));
      await user.click(budgets.getByRole("button", { name: "Confirm delete" }));

      expect(mockApi.budgets.remove).toHaveBeenCalledWith("b-team-daily");
      await waitFor(() => expect(mockApi.budgets.list).toHaveBeenCalledTimes(2));
    });

    it("should say so and reload when the budget was already deleted", async () => {
      mockApi.budgets.remove.mockRejectedValue(new ApiError(404, ""));
      const user = await renderLoaded();
      const budgets = region("Budgets");

      await user.click(budgets.getByRole("button", { name: "Delete Team Daily budget" }));
      await user.click(budgets.getByRole("button", { name: "Confirm delete" }));

      expect(await budgets.findByRole("alert")).toHaveTextContent("That item no longer exists. The list has been reloaded.");
      expect(mockApi.budgets.list).toHaveBeenCalledTimes(2);
    });
  });

  describe("developer plans", () => {
    it("should create a plan with no window limit when the limit is left blank", async () => {
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.type(plans.getByLabelText("Developer email"), " carol@x.io ");
      await user.type(plans.getByLabelText("Plan name"), "Pro");
      await user.type(plans.getByLabelText("Monthly price (USD)"), "20");
      await user.click(plans.getByRole("button", { name: "Add plan" }));

      expect(mockApi.developerPlans.create).toHaveBeenCalledWith({
        developerEmail: "carol@x.io",
        planName: "Pro",
        monthlyPriceUsd: 20,
        windowLimitUsd: null,
      });
      await waitFor(() => expect(mockApi.developerPlans.list).toHaveBeenCalledTimes(2));
    });

    it("should create a plan with its 5-hour window limit", async () => {
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.type(plans.getByLabelText("Developer email"), "carol@x.io");
      await user.type(plans.getByLabelText("Plan name"), "Max");
      await user.type(plans.getByLabelText("Monthly price (USD)"), "100");
      await user.type(plans.getByLabelText("5-hour window limit (USD, optional)"), "35");
      await user.click(plans.getByRole("button", { name: "Add plan" }));

      expect(mockApi.developerPlans.create).toHaveBeenCalledWith(expect.objectContaining({ windowLimitUsd: 35 }));
    });

    it("should explain that a developer already has a plan when the server returns 409", async () => {
      mockApi.developerPlans.create.mockRejectedValue(new ApiError(409, ""));
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.type(plans.getByLabelText("Developer email"), "alice@x.io");
      await user.type(plans.getByLabelText("Plan name"), "Pro");
      await user.type(plans.getByLabelText("Monthly price (USD)"), "20");
      await user.click(plans.getByRole("button", { name: "Add plan" }));

      expect(await plans.findByRole("alert")).toHaveTextContent(
        "alice@x.io already has a plan. Edit it instead.",
      );
    });

    it("should show the server's validation messages for a rejected plan", async () => {
      mockApi.developerPlans.create.mockRejectedValue(validation({ PlanName: ["Plan name is too long."] }));
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.type(plans.getByLabelText("Developer email"), "d@x.io");
      await user.type(plans.getByLabelText("Plan name"), "x");
      await user.type(plans.getByLabelText("Monthly price (USD)"), "20");
      await user.click(plans.getByRole("button", { name: "Add plan" }));

      expect(await plans.findByRole("alert")).toHaveTextContent("Plan name is too long.");
    });

    it("should edit a plan's name, price and limit, clearing the limit to null", async () => {
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.click(plans.getByRole("button", { name: "Edit plan for alice@x.io" }));
      const name = plans.getByLabelText("Plan name for alice@x.io");
      await user.clear(name);
      await user.type(name, "Max 5x");
      const price = plans.getByLabelText("Monthly price for alice@x.io");
      await user.clear(price);
      await user.type(price, "100");
      await user.clear(plans.getByLabelText("Window limit for alice@x.io"));
      await user.click(plans.getByRole("button", { name: "Save" }));

      expect(mockApi.developerPlans.update).toHaveBeenCalledWith("p-alice", {
        planName: "Max 5x",
        monthlyPriceUsd: 100,
        windowLimitUsd: null,
      });
    });

    it("should keep the editor open with the message when an edit fails", async () => {
      mockApi.developerPlans.update.mockRejectedValue(validation({ MonthlyPriceUsd: ["Price must be more than 0."] }));
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.click(plans.getByRole("button", { name: "Edit plan for alice@x.io" }));
      await user.click(plans.getByRole("button", { name: "Save" }));

      expect(await plans.findByRole("alert")).toHaveTextContent("Price must be more than 0.");
      expect(plans.getByRole("button", { name: "Save" })).toBeInTheDocument();
    });

    it("should delete a plan after confirmation", async () => {
      const user = await renderLoaded();
      const plans = region("Developer plans");

      await user.click(plans.getByRole("button", { name: "Delete plan for alice@x.io" }));
      await user.click(plans.getByRole("button", { name: "Confirm delete" }));

      expect(mockApi.developerPlans.remove).toHaveBeenCalledWith("p-alice");
    });

    it("should show a plan's limit, or none", async () => {
      mockApi.developerPlans.list.mockResolvedValue([
        buildDeveloperPlan(),
        buildDeveloperPlan({ id: "p-bob", developerEmail: "bob@x.io", windowLimitUsd: null }),
      ]);
      await renderLoaded();
      const plans = region("Developer plans");

      expect(within(plans.getByRole("cell", { name: "alice@x.io" }).closest("tr")!).getByText("$40.00")).toBeInTheDocument();
      expect(within(plans.getByRole("cell", { name: "bob@x.io" }).closest("tr")!).getByText("none")).toBeInTheDocument();
    });
  });
});
