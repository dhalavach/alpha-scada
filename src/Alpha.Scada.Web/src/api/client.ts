export const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";
export const tokenKey = "alpha_scada_token";

export function authHeaders(token: string) {
  return { Authorization: `Bearer ${token}` };
}

export async function getJson<T>(path: string, token: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, { headers: authHeaders(token) });
  if (!response.ok) throw new Error(`${path} failed`);
  return response.json();
}
