import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";

afterEach(() => {
  cleanup();
});

// Recharts uses ResizeObserver which jsdom does not provide
class ResizeObserverStub {
  observe = vi.fn();
  unobserve = vi.fn();
  disconnect = vi.fn();
}

vi.stubGlobal("ResizeObserver", ResizeObserverStub);

// jsdom does not implement scrollIntoView (used to jump to a governance-flagged timeline item)
Element.prototype.scrollIntoView = vi.fn();

// jsdom does not provide IntersectionObserver (used for fetch-on-scroll pagination). This stub
// lets tests manually simulate a sentinel scrolling into view via triggerIntersections().
type IntersectionCallback = (entries: Array<{ isIntersecting: boolean }>) => void;
const intersectionCallbacks: IntersectionCallback[] = [];

export function triggerIntersections(isIntersecting = true): void {
  for (const cb of intersectionCallbacks) cb([{ isIntersecting }]);
}

class IntersectionObserverStub {
  constructor(callback: IntersectionCallback) {
    intersectionCallbacks.push(callback);
  }
  observe = vi.fn();
  unobserve = vi.fn();
  disconnect = vi.fn();
}

vi.stubGlobal("IntersectionObserver", IntersectionObserverStub);

afterEach(() => {
  intersectionCallbacks.length = 0;
});

// jsdom does not provide window.matchMedia (used by useDarkMode hook)
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});
