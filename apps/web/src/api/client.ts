import type { ProblemDetails } from '../types/api';
import { getSession, setSession } from '../features/auth/session';

/**
 * Where the API lives. In development this stays '/api' and the Vite dev server proxies it
 * to the backend; set VITE_API_BASE_URL to point a build somewhere else. See .env.example.
 */
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api';

/** A failed HTTP call, carrying the problem document the API returned. */
export class ApiError extends Error {
  readonly status: number;

  readonly problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Field name to messages, when the failure was a validation failure. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {};
  }
}

/**
 * The single place that talks to the API. Returns parsed JSON, or throws an
 * {@link ApiError} that callers can show to the user.
 */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const session = path === '/auth/login' ? null : getSession();
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(session ? { Authorization: `Bearer ${session.accessToken}` } : {}),
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    if (response.status === 401 && session && getSession() === session) setSession(null);
    const problem = await readProblem(response);

    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? response.statusText,
      problem,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** Turns anything thrown by a fetch into a message worth showing a user. */
export function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.message;
  }

  if (error instanceof Error) {
    return `${fallback} (${error.message})`;
  }

  return fallback;
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    const value: unknown = await response.json();
    if (typeof value !== 'object' || value === null) return;
    const problem: ProblemDetails = {};
    if ('detail' in value && typeof value.detail === 'string') problem.detail = value.detail;
    if ('title' in value && typeof value.title === 'string') problem.title = value.title;
    if ('status' in value && typeof value.status === 'number') problem.status = value.status;
    if ('type' in value && typeof value.type === 'string') problem.type = value.type;
    if ('traceId' in value && typeof value.traceId === 'string') problem.traceId = value.traceId;
    if ('errors' in value && typeof value.errors === 'object' && value.errors !== null) {
      const errors: Record<string, string[]> = {};
      for (const [field, messages] of Object.entries(value.errors)) {
        if (Array.isArray(messages) && messages.every((message: unknown) => typeof message === 'string')) {
          errors[field] = messages;
        }
      }
      problem.errors = errors;
    }
    return problem;
  } catch {
    // Not every failure has a JSON body - a dead server certainly will not.
    return undefined;
  }
}
