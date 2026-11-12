// Server-only client for the .NET admin API. The admin key never reaches the browser.
import { env } from "./env";
import type { ApiConfig, BookingPage, Stats, TrackedBooking } from "./types";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${env.apiBaseUrl()}${path}`, {
      ...init,
      cache: "no-store",
      headers: { "X-Admin-Key": env.adminApiKey(), "Content-Type": "application/json", ...init?.headers },
    });
  } catch {
    throw new ApiError(503, `Can't reach the API at ${env.apiBaseUrl()}. Is it running?`);
  }
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    let message = text;
    try { message = JSON.parse(text).error ?? text; } catch {}
    throw new ApiError(res.status, message || `API returned ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export function toQuery(params: Record<string, string | undefined>) {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) q.set(k, v);
  const s = q.toString();
  return s ? `?${s}` : "";
}

export const api = {
  bookings: (params: Record<string, string | undefined>) => call<BookingPage>(`/api/admin/bookings${toQuery(params)}`),
  booking: (id: string) => call<TrackedBooking>(`/api/admin/bookings/${encodeURIComponent(id)}`),
  stats: (params: Record<string, string | undefined>) => call<Stats>(`/api/admin/stats${toQuery(params)}`),
  config: () => call<ApiConfig>(`/api/admin/config`),
  updateReconciliation: (id: string, body: { status?: string; notes?: string }) =>
    call<TrackedBooking>(`/api/admin/bookings/${encodeURIComponent(id)}/reconciliation`, { method: "PATCH", body: JSON.stringify(body) }),
  sendConversion: (id: string) =>
    call<{ conversionStatus: string; lastConversionError: string | null }>(`/api/admin/bookings/${encodeURIComponent(id)}/send-conversion`, { method: "POST" }),
  exportCsv: async (params: Record<string, string | undefined>) => {
    const res = await fetch(`${env.apiBaseUrl()}/api/admin/bookings/export${toQuery(params)}`, {
      cache: "no-store",
      headers: { "X-Admin-Key": env.adminApiKey() },
    });
    if (!res.ok) throw new ApiError(res.status, `Export failed (${res.status})`);
    return res;
  },
};
