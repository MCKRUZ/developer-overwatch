import { renderHook, act } from "@testing-library/react";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { useDarkMode } from "./useDarkMode";

describe("useDarkMode", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove("dark");
  });

  it("defaults to light when no localStorage and prefers-color-scheme is light", () => {
    vi.spyOn(window, "matchMedia").mockReturnValue({
      matches: false,
    } as MediaQueryList);

    const { result } = renderHook(() => useDarkMode());
    expect(result.current.isDark).toBe(false);
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });

  it("defaults to dark when no localStorage and prefers-color-scheme is dark", () => {
    vi.spyOn(window, "matchMedia").mockReturnValue({
      matches: true,
    } as MediaQueryList);

    const { result } = renderHook(() => useDarkMode());
    expect(result.current.isDark).toBe(true);
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("reads 'dark' from localStorage", () => {
    localStorage.setItem("overwatch-theme", "dark");

    const { result } = renderHook(() => useDarkMode());
    expect(result.current.isDark).toBe(true);
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("reads 'light' from localStorage even when system prefers dark", () => {
    localStorage.setItem("overwatch-theme", "light");
    vi.spyOn(window, "matchMedia").mockReturnValue({
      matches: true,
    } as MediaQueryList);

    const { result } = renderHook(() => useDarkMode());
    expect(result.current.isDark).toBe(false);
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });

  it("toggles from light to dark", () => {
    vi.spyOn(window, "matchMedia").mockReturnValue({
      matches: false,
    } as MediaQueryList);

    const { result } = renderHook(() => useDarkMode());
    expect(result.current.isDark).toBe(false);

    act(() => {
      result.current.toggle();
    });

    expect(result.current.isDark).toBe(true);
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(localStorage.getItem("overwatch-theme")).toBe("dark");
  });

  it("toggles from dark to light", () => {
    localStorage.setItem("overwatch-theme", "dark");

    const { result } = renderHook(() => useDarkMode());
    expect(result.current.isDark).toBe(true);

    act(() => {
      result.current.toggle();
    });

    expect(result.current.isDark).toBe(false);
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(localStorage.getItem("overwatch-theme")).toBe("light");
  });

  it("persists choice to localStorage on each toggle", () => {
    vi.spyOn(window, "matchMedia").mockReturnValue({
      matches: false,
    } as MediaQueryList);

    const { result } = renderHook(() => useDarkMode());

    act(() => {
      result.current.toggle();
    });
    expect(localStorage.getItem("overwatch-theme")).toBe("dark");

    act(() => {
      result.current.toggle();
    });
    expect(localStorage.getItem("overwatch-theme")).toBe("light");
  });
});
