import { describe, it, expect } from "vitest";
import { categoryLabel, contextLevel, formatCompactTokens, formatFill } from "./contextFormat";

describe("formatCompactTokens", () => {
  it("should show small counts in full so a handful of tokens never reads as 0K", () => {
    expect(formatCompactTokens(0)).toBe("0");
    expect(formatCompactTokens(999)).toBe("999");
  });

  it("should show thousands as whole K", () => {
    expect(formatCompactTokens(5_000)).toBe("5K");
    expect(formatCompactTokens(148_400)).toBe("148K");
    expect(formatCompactTokens(392_600)).toBe("393K");
  });

  it("should roll over to M instead of printing 1000K", () => {
    expect(formatCompactTokens(999_500)).toBe("1.0M");
    expect(formatCompactTokens(1_000_000)).toBe("1.0M");
    expect(formatCompactTokens(2_250_000)).toBe("2.3M");
  });
});

describe("formatFill", () => {
  it("should floor, so a fill just under a threshold never reads as the threshold", () => {
    expect(formatFill(0.749)).toBe("74%");
    expect(formatFill(0.17)).toBe("17%");
  });

  it("should show a fill above 100% instead of hiding an outdated window size", () => {
    expect(formatFill(1.04)).toBe("104%");
  });
});

describe("contextLevel", () => {
  it("should use the Usage page thresholds: amber from 75%, red from 90%", () => {
    expect(contextLevel(0.7499)).toBe("Ok");
    expect(contextLevel(0.75)).toBe("Amber");
    expect(contextLevel(0.8999)).toBe("Amber");
    expect(contextLevel(0.9)).toBe("Red");
    expect(contextLevel(1.2)).toBe("Red");
  });
});

describe("categoryLabel", () => {
  it("should give every category a friendly name", () => {
    expect(categoryLabel({ category: "systemPrompt" })).toBe("System prompt");
    expect(categoryLabel({ category: "builtInTools" })).toBe("Built-in tools");
    expect(categoryLabel({ category: "mcpInstructions" })).toBe("MCP server instructions");
    expect(categoryLabel({ category: "customAgents" })).toBe("Custom agents");
    expect(categoryLabel({ category: "memoryFiles" })).toBe("Memory files");
    expect(categoryLabel({ category: "skills" })).toBe("Skills");
    expect(categoryLabel({ category: "conversation" })).toBe("Conversation");
  });

  it("should name the server on an MCP tools row, since there is one row per server", () => {
    expect(categoryLabel({ category: "mcpTools", server: "github" })).toBe("MCP tools (github)");
    expect(categoryLabel({ category: "mcpTools", server: null })).toBe("MCP tools");
  });
});
