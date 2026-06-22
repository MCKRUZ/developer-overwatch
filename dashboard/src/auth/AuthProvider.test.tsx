import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";

const oidc = vi.hoisted(() => ({
  auth: {
    isAuthenticated: false,
    isLoading: false,
    user: null as { access_token: string } | null,
    signinRedirect: vi.fn(),
    signoutRedirect: vi.fn(),
  },
}));

vi.mock("react-oidc-context", () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useAuth: () => oidc.auth,
}));

async function loadModule() {
  return await import("./AuthProvider");
}

function Consumer({ hook }: { hook: () => ReturnType<typeof import("./AuthProvider").useAuthContext> }) {
  const ctx = hook();
  return (
    <div>
      <span data-testid="authed">{String(ctx.isAuthenticated)}</span>
      <span data-testid="loading">{String(ctx.isLoading)}</span>
      <span data-testid="token">{ctx.token ?? "none"}</span>
      <button type="button" onClick={ctx.login}>
        login
      </button>
      <button type="button" onClick={ctx.logout}>
        logout
      </button>
    </div>
  );
}

describe("AuthProvider", () => {
  beforeEach(() => {
    vi.resetModules();
    oidc.auth = {
      isAuthenticated: false,
      isLoading: false,
      user: null,
      signinRedirect: vi.fn(),
      signoutRedirect: vi.fn(),
    };
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  describe("when OIDC is not configured (dev mode)", () => {
    it("reports isAuthConfigured false and renders children with default context", async () => {
      const { default: AuthProvider, useAuthContext, isAuthConfigured } = await loadModule();
      expect(isAuthConfigured).toBe(false);

      render(
        <AuthProvider>
          <Consumer hook={useAuthContext} />
        </AuthProvider>,
      );

      expect(screen.getByTestId("authed")).toHaveTextContent("false");
      expect(screen.getByTestId("token")).toHaveTextContent("none");
    });
  });

  describe("when OIDC is configured", () => {
    beforeEach(() => {
      vi.stubEnv("VITE_OIDC_AUTHORITY", "https://idp.test");
      vi.stubEnv("VITE_OIDC_CLIENT_ID", "client-123");
    });

    it("reports isAuthConfigured true", async () => {
      const { isAuthConfigured } = await loadModule();
      expect(isAuthConfigured).toBe(true);
    });

    it("maps the OIDC user into context and exposes the access token", async () => {
      oidc.auth.isAuthenticated = true;
      oidc.auth.user = { access_token: "tok-abc" };
      const { default: AuthProvider, useAuthContext } = await loadModule();

      render(
        <AuthProvider>
          <Consumer hook={useAuthContext} />
        </AuthProvider>,
      );

      expect(screen.getByTestId("authed")).toHaveTextContent("true");
      expect(screen.getByTestId("token")).toHaveTextContent("tok-abc");
    });

    it("wires login and logout to the OIDC redirects", async () => {
      const { default: AuthProvider, useAuthContext } = await loadModule();

      render(
        <AuthProvider>
          <Consumer hook={useAuthContext} />
        </AuthProvider>,
      );

      await userEvent.click(screen.getByRole("button", { name: "login" }));
      await userEvent.click(screen.getByRole("button", { name: "logout" }));

      expect(oidc.auth.signinRedirect).toHaveBeenCalledTimes(1);
      expect(oidc.auth.signoutRedirect).toHaveBeenCalledTimes(1);
    });
  });
});
