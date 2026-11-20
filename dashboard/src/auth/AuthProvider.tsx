import { AuthProvider as OidcAuthProvider } from "react-oidc-context";
import type { User } from "oidc-client-ts";
import type { ReactNode } from "react";
import { createContext, useContext, useCallback, useMemo, useState } from "react";
import { useAuth } from "react-oidc-context";

interface AuthContextValue {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: User | null;
  token: string | null;
  /** Why the last sign-in attempt could not leave the page, or null. */
  loginError: string | null;
  login: () => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue>({
  isAuthenticated: false,
  isLoading: false,
  user: null,
  token: null,
  loginError: null,
  login: () => {},
  logout: () => {},
});

export const useAuthContext = () => useContext(AuthContext);

const authority = import.meta.env.VITE_OIDC_AUTHORITY;
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID;

export const isAuthConfigured = Boolean(authority && clientId);

const oidcConfig = isAuthConfigured
  ? {
      authority: authority!,
      client_id: clientId!,
      redirect_uri: import.meta.env.VITE_OIDC_REDIRECT_URI ?? window.location.origin,
      scope: import.meta.env.VITE_OIDC_SCOPE ?? "openid profile email",
      automaticSilentRenew: true,
    }
  : null;

// The redirect first fetches the sign-in server's settings from the page; a browser extension or network filter that
// blocks that host makes it reject before the page ever leaves, which otherwise looks like a dead button.
const signInHost = authority ? new URL(authority).host : "";
const signInUnreachableMessage =
  `Couldn't reach the sign-in server (${signInHost}). A browser extension or network filter may be blocking it. ` +
  "Try a private window or another browser, or allow that site in your blocker.";

function InnerAuthProvider({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const [loginError, setLoginError] = useState<string | null>(null);

  const login = useCallback(() => {
    setLoginError(null);
    auth.signinRedirect().catch(() => setLoginError(signInUnreachableMessage));
  }, [auth]);

  const logout = useCallback(() => {
    void auth.signoutRedirect();
  }, [auth]);

  const value = useMemo<AuthContextValue>(
    () => ({
      isAuthenticated: auth.isAuthenticated,
      isLoading: auth.isLoading,
      user: auth.user ?? null,
      token: auth.user?.access_token ?? null,
      loginError,
      login,
      logout,
    }),
    [auth.isAuthenticated, auth.isLoading, auth.user, loginError, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

const devValue: AuthContextValue = {
  isAuthenticated: false,
  isLoading: false,
  user: null,
  token: null,
  loginError: null,
  login: () => {},
  logout: () => {},
};

export default function AuthProvider({ children }: { children: ReactNode }) {
  if (!isAuthConfigured || !oidcConfig) {
    return <AuthContext.Provider value={devValue}>{children}</AuthContext.Provider>;
  }

  return (
    <OidcAuthProvider {...oidcConfig}>
      <InnerAuthProvider>{children}</InnerAuthProvider>
    </OidcAuthProvider>
  );
}
