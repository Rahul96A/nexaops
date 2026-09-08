import type { AuthenticationResult, ProblemDetails } from './types';

/**
 * The single HTTP client for the NexaOps API.
 *
 * Everything the application sends to the server goes through here, which is what makes three
 * things true in one place: the access token is attached consistently, an expired token is
 * refreshed once and transparently, and every failure arrives as a typed {@link ApiError}
 * rather than a raw response the caller has to interpret.
 */

const API_PREFIX = '/api/v1';

/** Where tokens live between page loads. */
const ACCESS_TOKEN_KEY = 'nexaops.accessToken';
const REFRESH_TOKEN_KEY = 'nexaops.refreshToken';

/**
 * An API failure, carrying the server's problem details.
 *
 * The `code` is the stable identifier a caller should branch on - never the message, which is
 * written for a person and may be reworded.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}.`);
    this.name = 'ApiError';
  }

  get code(): string {
    return this.problem.code ?? 'unknown_error';
  }

  /** Field-level validation messages, when the failure was a validation failure. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }

  get isValidationError(): boolean {
    return this.status === 400 && Object.keys(this.fieldErrors).length > 0;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  get isConflict(): boolean {
    return this.status === 409;
  }

  /** True when AI is simply not configured in this environment. */
  get isAiUnavailable(): boolean {
    return this.status === 503 && this.code === 'ai_not_configured';
  }

  /**
   * A message safe and useful to show a user. Validation failures are summarised from the
   * field errors, because the top-level detail on those is deliberately generic.
   */
  get userMessage(): string {
    if (this.isValidationError) {
      const messages = Object.values(this.fieldErrors).flat();
      return messages.length > 0 ? messages.join(' ') : this.message;
    }

    return this.message;
  }
}

// ---------------------------------------------------------------------------
// Token storage
// ---------------------------------------------------------------------------

export const tokenStore = {
  getAccessToken: (): string | null => safeRead(ACCESS_TOKEN_KEY),
  getRefreshToken: (): string | null => safeRead(REFRESH_TOKEN_KEY),

  set(result: AuthenticationResult): void {
    safeWrite(ACCESS_TOKEN_KEY, result.accessToken);
    safeWrite(REFRESH_TOKEN_KEY, result.refreshToken);
  },

  clear(): void {
    safeRemove(ACCESS_TOKEN_KEY);
    safeRemove(REFRESH_TOKEN_KEY);
  },
};

/**
 * Storage access is wrapped because it throws outright in a private window or when a browser
 * is configured to block site data. A sign-in that fails because storage is unavailable should
 * degrade to a session that ends on refresh, not to a blank screen.
 */
function safeRead(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeWrite(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Nothing to do: the session simply will not survive a reload.
  }
}

function safeRemove(key: string): void {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // Ignored for the same reason.
  }
}

// ---------------------------------------------------------------------------
// Session expiry
// ---------------------------------------------------------------------------

type SessionExpiredHandler = () => void;

let onSessionExpired: SessionExpiredHandler = () => {};

/** Registered by the auth provider so an unrecoverable 401 returns the user to sign-in. */
export function setSessionExpiredHandler(handler: SessionExpiredHandler): void {
  onSessionExpired = handler;
}

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE';
  body?: unknown;
  /** Query parameters. Arrays are serialised as repeated keys; null and undefined are dropped. */
  query?: Record<string, unknown>;
  signal?: AbortSignal;
  /** Set for the auth endpoints themselves, which must not attempt a token refresh. */
  skipAuthRefresh?: boolean;
}

/**
 * Only one refresh is ever in flight. Without this, a page that fires six queries on mount
 * would attempt six simultaneous refreshes, and rotation means five of them would present an
 * already-rotated token - which the server correctly treats as theft and responds to by
 * revoking the whole session.
 */
let refreshInFlight: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = tokenStore.getRefreshToken();
  if (!refreshToken) {
    return false;
  }

  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(`${API_PREFIX}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });

      if (!response.ok) {
        tokenStore.clear();
        return false;
      }

      tokenStore.set((await response.json()) as AuthenticationResult);
      return true;
    } catch {
      return false;
    } finally {
      // Cleared on the next tick so concurrent callers all observe this same result.
      queueMicrotask(() => {
        refreshInFlight = null;
      });
    }
  })();

  return refreshInFlight;
}

function buildQueryString(query: Record<string, unknown> | undefined): string {
  if (!query) {
    return '';
  }

  const params = new URLSearchParams();

  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }

    if (Array.isArray(value)) {
      for (const item of value) {
        if (item !== undefined && item !== null && item !== '') {
          params.append(key, String(item));
        }
      }
      continue;
    }

    params.append(key, String(value));
  }

  const serialised = params.toString();
  return serialised ? `?${serialised}` : '';
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};

  try {
    const text = await response.text();
    if (text) {
      problem = JSON.parse(text) as ProblemDetails;
    }
  } catch {
    // A non-JSON error body (a proxy error page, say) still becomes a typed failure.
    problem = { title: response.statusText };
  }

  return new ApiError(response.status, problem);
}

async function send<T>(path: string, options: RequestOptions, isRetry: boolean): Promise<T> {
  const { method = 'GET', body, query, signal, skipAuthRefresh } = options;

  const headers: Record<string, string> = { Accept: 'application/json' };

  const accessToken = tokenStore.getAccessToken();
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(`${API_PREFIX}${path}${buildQueryString(query)}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  // A 401 on a token we hold means it has expired or been invalidated. Refresh once and
  // replay; a second failure means the session is genuinely over.
  if (response.status === 401 && !isRetry && !skipAuthRefresh && accessToken) {
    if (await refreshAccessToken()) {
      return send<T>(path, options, true);
    }

    tokenStore.clear();
    onSessionExpired();
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T;
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return send<T>(path, options, false);
}

/**
 * Downloads a file from an authenticated endpoint.
 *
 * A plain link cannot be used: the API takes a bearer token and a browser navigation carries no
 * Authorization header, so the request has to be made here and handed to the browser as a blob.
 * The object URL is revoked immediately afterwards — leaving it alive pins the whole file in
 * memory for the life of the tab.
 */
export async function downloadFile(path: string, query?: Record<string, unknown>): Promise<void> {
  const headers: Record<string, string> = {};

  const accessToken = tokenStore.getAccessToken();
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  const response = await fetch(`${API_PREFIX}${path}${buildQueryString(query)}`, { headers });

  if (!response.ok) {
    throw await toApiError(response);
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);

  try {
    const link = document.createElement('a');
    link.href = url;
    link.download = fileNameFrom(response) ?? 'download.csv';
    document.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}

/** Reads the server's suggested file name, so the download is named the same as the export. */
function fileNameFrom(response: Response): string | null {
  const disposition = response.headers.get('Content-Disposition');

  if (!disposition) {
    return null;
  }

  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  return match ? decodeURIComponent(match[1]) : null;
}

export const api = {
  get: <T>(path: string, query?: Record<string, unknown>, signal?: AbortSignal) =>
    apiRequest<T>(path, { method: 'GET', query, signal }),

  post: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    apiRequest<T>(path, { ...options, method: 'POST', body }),

  patch: <T>(path: string, body?: unknown) => apiRequest<T>(path, { method: 'PATCH', body }),

  put: <T>(path: string, body?: unknown) => apiRequest<T>(path, { method: 'PUT', body }),

  delete: <T>(path: string, body?: unknown) => apiRequest<T>(path, { method: 'DELETE', body }),
};
