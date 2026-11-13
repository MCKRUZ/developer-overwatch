import { ApiError } from "../api";

interface ProblemBody {
  title?: unknown;
  errors?: unknown;
}

function parseProblem(body: string): ProblemBody {
  try {
    const parsed: unknown = JSON.parse(body);
    return parsed && typeof parsed === "object" ? (parsed as ProblemBody) : {};
  } catch {
    return {};
  }
}

/** The messages of an RFC 7807 validation problem, `errors: { Field: ["message", ...] }`. */
function validationMessages(body: string): string[] {
  const { errors } = parseProblem(body);
  if (!errors || typeof errors !== "object") return [];
  return Object.values(errors as Record<string, unknown>)
    .flatMap((messages) => (Array.isArray(messages) ? messages : []))
    .filter((m): m is string => typeof m === "string");
}

/**
 * What to tell the user after a failed create, edit or delete. A 409 always gets `conflictMessage`,
 * so an empty body still says what to do; a 400 lists the server's validation messages.
 */
export function mutationErrorMessage(error: unknown, conflictMessage: string): string {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : String(error);
  switch (error.status) {
    case 409:
      return conflictMessage;
    case 400: {
      const messages = validationMessages(error.body);
      return messages.length > 0 ? messages.join(" ") : "The server rejected these values.";
    }
    case 403:
      return "You are not allowed to edit budgets and plans.";
    case 404:
      return "That item no longer exists. The list has been reloaded.";
    default:
      return error.message;
  }
}
