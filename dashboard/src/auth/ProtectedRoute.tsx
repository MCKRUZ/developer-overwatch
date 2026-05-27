import type { ReactNode } from "react";
import { isAuthConfigured, useAuthContext } from "./AuthProvider";
import LoginPage from "./LoginPage";

function Spinner() {
  return (
    <div className="flex items-center justify-center min-h-screen">
      <div className="h-8 w-8 animate-spin rounded-full border-4 border-gray-300 dark:border-gray-600 border-t-blue-600" />
    </div>
  );
}

export default function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuthContext();

  if (!isAuthConfigured) {
    return <>{children}</>;
  }

  if (isLoading) {
    return <Spinner />;
  }

  if (!isAuthenticated) {
    return <LoginPage />;
  }

  return <>{children}</>;
}
