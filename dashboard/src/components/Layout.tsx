import { NavLink, Outlet } from "react-router-dom";
import { isAuthConfigured, useAuthContext } from "../auth/AuthProvider";
import { useDarkMode } from "../hooks/useDarkMode";

const navItems = [
  { to: "/", label: "PR Queue" },
  { to: "/sessions", label: "Sessions" },
  { to: "/governance", label: "Governance" },
  { to: "/analytics", label: "Analytics" },
  { to: "/usage", label: "Usage" },
  { to: "/settings", label: "Settings" },
];

function UserInfo() {
  const { user, logout } = useAuthContext();

  if (!isAuthConfigured) {
    return (
      <span className="rounded bg-yellow-600 px-2 py-1 text-xs font-medium text-yellow-100">
        Dev Mode
      </span>
    );
  }

  const displayName =
    user?.profile?.email ?? user?.profile?.name ?? "User";

  return (
    <div className="flex items-center gap-3">
      <span className="text-sm text-gray-300">{displayName}</span>
      <button
        type="button"
        onClick={logout}
        className="rounded bg-gray-700 px-2.5 py-1 text-xs font-medium text-gray-300 transition-colors hover:bg-gray-600 hover:text-white"
      >
        Sign out
      </button>
    </div>
  );
}

export default function Layout() {
  const { isDark, toggle } = useDarkMode();

  return (
    <div className="min-h-screen flex flex-col">
      <header className="bg-gray-900 text-white">
        <div className="max-w-7xl mx-auto px-4 py-3 flex items-center justify-between">
          <h1 className="text-lg font-semibold tracking-tight">Developer Overwatch</h1>
          <div className="flex items-center gap-4">
            <nav className="flex gap-1">
              {navItems.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === "/"}
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded text-sm font-medium transition-colors ${
                      isActive
                        ? "bg-gray-700 text-white"
                        : "text-gray-300 hover:text-white hover:bg-gray-800"
                    }`
                  }
                >
                  {item.label}
                </NavLink>
              ))}
            </nav>
            <button
              type="button"
              onClick={toggle}
              className="rounded px-2 py-1.5 text-sm text-gray-300 transition-colors hover:text-white hover:bg-gray-800"
              aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
            >
              {isDark ? "☀️" : "🌙"}
            </button>
            <UserInfo />
          </div>
        </div>
      </header>
      <main className="flex-1 max-w-7xl mx-auto w-full px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
