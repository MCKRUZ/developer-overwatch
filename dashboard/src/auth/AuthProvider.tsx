import { AuthProvider as OidcAuthProvider } from "react-oidc-context";
import type { User } from "oidc-client-ts";
import type { ReactNode } from "react";
import { createContext, useContext, useCallback, useMemo } from "react";
import { useAuth } from "react-oidc-context";

interface AuthContextValue {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: User | null;
  token: string | null;
  login: () => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue>({
  isAuthenticated: false,
  isLoading: false,
  user: null,
  token: null,
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

function InnerAuthProvider({ children }: { children: ReactNode }) {
  const auth = useAuth();

  const login = useCallback(() => {
    void auth.signinRedirect();
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
      login,
      logout,
    }),
    [auth.isAuthenticated, auth.isLoading, auth.user, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

const devValue: AuthContextValue = {
  isAuthenticated: false,
  isLoading: false,
  user: null,
  token: null,
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
