import { describe, it, expect } from "vitest";
import { ApiError } from "../api";
import { mutationErrorMessage } from "./apiErrorMessage";

const CONFLICT = "Already there.";

describe("mutationErrorMessage", () => {
  it("should give the caller's message for a 409 whatever the body says", () => {
    expect(mutationErrorMessage(new ApiError(409, '{"title":"Conflict"}'), CONFLICT)).toBe(CONFLICT);
  });

  it("should list every field's validation messages from a 400 problem", () => {
    const body = JSON.stringify({ title: "x", status: 400, errors: { A: ["one.", "two."], B: ["three."] } });
    expect(mutationErrorMessage(new ApiError(400, body), CONFLICT)).toBe("one. two. three.");
  });

  it.each([
    ["no errors member", JSON.stringify({ title: "Bad" })],
    ["a non-JSON body", "Bad Request"],
    ["a JSON scalar", "42"],
    ["errors that are not message lists", JSON.stringify({ errors: { A: "flat", B: [1] } })],
  ])("should fall back to a generic message for a 400 with %s", (_, body) => {
    expect(mutationErrorMessage(new ApiError(400, body), CONFLICT)).toBe("The server rejected these values.");
  });

  it("should say the viewer may not edit on a 403", () => {
    expect(mutationErrorMessage(new ApiError(403, ""), CONFLICT)).toBe("You are not allowed to edit budgets and plans.");
  });

  it("should pass through other failures", () => {
    expect(mutationErrorMessage(new ApiError(500, "boom"), CONFLICT)).toBe("500: boom");
    expect(mutationErrorMessage(new TypeError("Failed to fetch"), CONFLICT)).toBe("Failed to fetch");
    expect(mutationErrorMessage("odd", CONFLICT)).toBe("odd");
  });
});
