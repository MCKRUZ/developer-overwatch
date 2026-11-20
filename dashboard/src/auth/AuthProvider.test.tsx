import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";

const oidc = vi.hoisted(() => ({
  auth: {
    isAuthenticated: false,
    isLoading: false,
    user: null as { access_token: string } | null,
    signinRedirect: vi.fn().mockResolvedValue(undefined),
    signoutRedirect: vi.fn().mockResolvedValue(undefined),
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
      <span data-testid="login-error">{ctx.loginError ?? "none"}</span>
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
      // The real redirects return promises; a bare vi.fn() returns undefined and hides how the app handles them.
      signinRedirect: vi.fn().mockResolvedValue(undefined),
      signoutRedirect: vi.fn().mockResolvedValue(undefined),
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

    // A browser extension that blocks the sign-in server makes the redirect reject before leaving the page;
    // without a message the button looks dead.
    it("reports a sign-in error naming the sign-in server when the redirect cannot start", async () => {
      oidc.auth.signinRedirect = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
      const { default: AuthProvider, useAuthContext } = await loadModule();

      render(
        <AuthProvider>
          <Consumer hook={useAuthContext} />
        </AuthProvider>,
      );

      expect(screen.getByTestId("login-error")).toHaveTextContent("none");
      await userEvent.click(screen.getByRole("button", { name: "login" }));

      expect(await screen.findByText(/couldn't reach the sign-in server \(idp\.test\)/i)).toBeInTheDocument();
    });

    it("clears an earlier sign-in error when a retry starts", async () => {
      oidc.auth.signinRedirect = vi
        .fn()
        .mockRejectedValueOnce(new TypeError("Failed to fetch"))
        .mockReturnValueOnce(new Promise(() => {}));
      const { default: AuthProvider, useAuthContext } = await loadModule();

      render(
        <AuthProvider>
          <Consumer hook={useAuthContext} />
        </AuthProvider>,
      );

      await userEvent.click(screen.getByRole("button", { name: "login" }));
      await screen.findByText(/couldn't reach the sign-in server/i);
      await userEvent.click(screen.getByRole("button", { name: "login" }));

      expect(screen.getByTestId("login-error")).toHaveTextContent("none");
    });
  });
});
