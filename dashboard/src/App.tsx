import { useEffect } from "react";
import { Routes, Route } from "react-router-dom";
import Layout from "./components/Layout";
import PrQueuePage from "./pages/PrQueuePage";
import SessionsPage from "./pages/SessionsPage";
import SessionDetailPage from "./pages/SessionDetailPage";
import AnalyticsPage from "./pages/AnalyticsPage";
import GovernancePage from "./pages/GovernancePage";
import UsagePage from "./pages/UsagePage";
import AuthProvider, { useAuthContext, isAuthConfigured } from "./auth/AuthProvider";
import ProtectedRoute from "./auth/ProtectedRoute";
import { setTokenGetter, clearTokenGetter } from "./api";

function TokenSync() {
  const { token } = useAuthContext();

  useEffect(() => {
    if (token) {
      setTokenGetter(() => token);
    } else {
      clearTokenGetter();
    }
    return () => clearTokenGetter();
  }, [token]);

  return null;
}

function AppRoutes() {
  return (
    <ProtectedRoute>
      {isAuthConfigured && <TokenSync />}
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<PrQueuePage />} />
          <Route path="/sessions" element={<SessionsPage />} />
          <Route path="/sessions/:sessionId" element={<SessionDetailPage />} />
          <Route path="/governance" element={<GovernancePage />} />
          <Route path="/analytics" element={<AnalyticsPage />} />
          <Route path="/usage" element={<UsagePage />} />
        </Route>
      </Routes>
    </ProtectedRoute>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  );
}
