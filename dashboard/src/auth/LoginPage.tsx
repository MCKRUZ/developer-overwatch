import { useAuthContext } from "./AuthProvider";

export default function LoginPage() {
  const { login } = useAuthContext();

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50">
      <div className="w-full max-w-sm rounded-lg bg-white p-8 shadow-md text-center">
        <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-gray-900 text-white text-2xl font-bold">
          DO
        </div>
        <h1 className="mb-2 text-xl font-semibold text-gray-900">Developer Overwatch</h1>
        <p className="mb-6 text-sm text-gray-500">Sign in to access the dashboard</p>
        <button
          type="button"
          onClick={login}
          className="w-full rounded-md bg-blue-600 px-4 py-2.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
        >
          Sign in
        </button>
      </div>
    </div>
  );
}
