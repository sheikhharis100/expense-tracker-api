import { trackRequest } from './slowRequests';

const TOKEN_KEY = 'expense-tracker.token';

// Identifies this tab. The API echoes it on live updates so a tab can skip refreshing for its own edits.
export const clientId: string =
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

let memoryToken: string | null = null;
const listeners = new Set<(token: string | null) => void>();

export function getToken(): string | null {
  if (memoryToken) return memoryToken;
  try {
    memoryToken = localStorage.getItem(TOKEN_KEY);
  } catch {
    // Storage blocked (private mode): the session lives in memory only.
  }
  return memoryToken;
}

export function setToken(token: string | null) {
  memoryToken = token;
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token);
    else localStorage.removeItem(TOKEN_KEY);
  } catch {
    // See getToken.
  }
  listeners.forEach((fn) => fn(token));
}

// The signed-in user's id, read from the token (no request needed). Used to keep per-user data on the device apart.
export function currentUserId(): string | null {
  const token = getToken();
  if (!token) return null;
  try {
    const part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const claims = JSON.parse(atob(part.padEnd(part.length + ((4 - (part.length % 4)) % 4), '=')));
    const id = claims.nameid ?? claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
    return id == null ? null : String(id);
  } catch {
    return null;
  }
}

export function onTokenChange(fn: (token: string | null) => void) {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    // For 409s: the latest server copy, or what blocks the change.
    public detail?: unknown,
  ) {
    super(message);
  }
}

// The request never got an answer (no signal, connection dropped). Not an ApiError, so queries still retry it.
export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super("Can't reach the server. Check your connection.", { cause });
  }
}

// The API answers { error, current? }, plain text, or ProblemDetails for validation errors (which also carry
// `error`: the first problem in plain words).
export async function readError(res: Response): Promise<{ message: string; detail?: unknown }> {
  const text = await res.text();
  if (!text) {
    if (res.status === 401) return { message: 'Please sign in again.' };
    if (res.status === 413) return { message: 'That upload is too large.' };
    return { message: `Request failed (${res.status})` };
  }
  try {
    const body = JSON.parse(text);
    if (typeof body === 'string') return { message: body };
    if (typeof body?.error === 'string' && body.error) return { message: body.error, detail: body.current };
    if (body?.errors) {
      const first = Object.values(body.errors as Record<string, string[]>).flat()[0];
      if (first) return { message: first };
    }
    return { message: body?.error ?? body?.title ?? text, detail: body?.current };
  } catch {
    return { message: text };
  }
}

type Options = { method?: string; body?: unknown; form?: FormData; auth?: boolean };

export async function api<T>(path: string, { method = 'GET', body, form, auth = true }: Options = {}): Promise<T> {
  const headers: Record<string, string> = { 'X-Client-Id': clientId };
  const token = getToken();
  if (auth && token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  const done = trackRequest();
  try {
    let res: Response;
    try {
      res = await fetch(path, {
        method,
        headers,
        body: form ?? (body !== undefined ? JSON.stringify(body) : undefined),
      });
    } catch (err) {
      throw new NetworkError(err);
    }

    if (res.status === 401 && auth) {
      // Only if the refused token is still the current one: after a password change this device already holds the
      // new token, and a request that left with the old one mustn't sign it out.
      if (getToken() === token) setToken(null);
      throw new ApiError('Your session has ended. Please sign in again.', 401);
    }
    if (!res.ok) {
      const { message, detail } = await readError(res);
      throw new ApiError(message, res.status, detail);
    }
    if (res.status === 204) return undefined as T;

    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as T;
  } finally {
    done();
  }
}
