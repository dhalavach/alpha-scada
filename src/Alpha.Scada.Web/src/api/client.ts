export const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";
export const tokenKey = "alpha_scada_token";

let unauthorizedHandler: (() => void) | undefined;

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly path: string) {
    super(`${path} failed with status ${status}`);
    this.name = "ApiError";
  }
}

export function setUnauthorizedHandler(handler?: () => void) {
  unauthorizedHandler = handler;
}

export function authHeaders(token: string) {
  return { Authorization: `Bearer ${token}` };
}

export async function getJson<T>(path: string, token: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, { headers: authHeaders(token) });
  ensureSuccess(response, path);
  return response.json();
}

export async function postJson(path: string, token: string, body?: unknown): Promise<void> {
  const response = await fetch(`${apiBase}${path}`, {
    method: "POST",
    headers: body === undefined
      ? authHeaders(token)
      : { ...authHeaders(token), "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  ensureSuccess(response, path);
}

function ensureSuccess(response: Response, path: string) {
  if (response.ok) return;
  if (response.status === 401) {
    unauthorizedHandler?.();
  }

  throw new ApiError(response.status, path);
}
