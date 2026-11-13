// Server-only client for the .NET admin API. The admin key never reaches the browser.
// A signed-in site user's calls carry X-Site-Scope, so the API itself limits them to their site, read-only.
import { env } from "./env";
import { getSession } from "./session";
import type {
  AffiliatePaymentStatus, ApiConfig, BookingPage, ConversionKind, ConversionStatus, Site, SiteInput, SiteUser, SiteUserInput, Stats, TrackedBooking,
} from "./types";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function authHeaders(): Promise<Record<string, string>> {
  const h: Record<string, string> = { "X-Admin-Key": env.adminApiKey() };
  const s = await getSession();
  if (s?.role === "site") h["X-Site-Scope"] = s.siteId;
  return h;
}

async function call<T>(path: string, init?: RequestInit, opts?: { unscoped?: boolean }): Promise<T> {
  let res: Response;
  const auth = opts?.unscoped ? { "X-Admin-Key": env.adminApiKey() } : await authHeaders();
  try {
    res = await fetch(`${env.apiBaseUrl()}${path}`, {
      ...init,
      cache: "no-store",
      headers: { ...auth, "Content-Type": "application/json", ...init?.headers },
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
  if (res.status === 204) return undefined as T;
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
  sendConversion: (id: string, kind: ConversionKind) =>
    call<{ status: ConversionStatus; error: string | null }>(
      `/api/admin/bookings/${encodeURIComponent(id)}/conversions/${kind.toLowerCase()}/send`, { method: "POST" }),
  setAffiliatePayment: (id: string, status: AffiliatePaymentStatus) =>
    call<{ affiliatePaymentStatus: AffiliatePaymentStatus; affiliatePaymentDate: string | null }>(
      `/api/admin/bookings/${encodeURIComponent(id)}/affiliate-payment`, { method: "PATCH", body: JSON.stringify({ status }) }),
  sites: () => call<Site[]>(`/api/admin/sites`),
  site: (id: string) => call<Site>(`/api/admin/sites/${encodeURIComponent(id)}`),
  createSite: (body: SiteInput) => call<Site>(`/api/admin/sites`, { method: "POST", body: JSON.stringify(body) }),
  updateSite: (id: string, body: SiteInput) => call<Site>(`/api/admin/sites/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(body) }),
  siteUsers: (siteId: string) => call<SiteUser[]>(`/api/admin/sites/${encodeURIComponent(siteId)}/users`),
  createSiteUser: (siteId: string, body: SiteUserInput) =>
    call<SiteUser>(`/api/admin/sites/${encodeURIComponent(siteId)}/users`, { method: "POST", body: JSON.stringify(body) }),
  updateSiteUser: (id: string, body: SiteUserInput) =>
    call<SiteUser>(`/api/admin/site-users/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(body) }),
  deleteSiteUser: (id: string) => call<void>(`/api/admin/site-users/${encodeURIComponent(id)}`, { method: "DELETE" }),
  /** Checks a site user's email and password. Throws ApiError 401 when wrong. */
  siteLogin: (email: string, password: string) =>
    call<{ user: SiteUser; site: { id: string; key: string; name: string } }>(
      `/api/admin/auth/site-login`, { method: "POST", body: JSON.stringify({ email, password }) }, { unscoped: true }),
  exportCsv: async (params: Record<string, string | undefined>) => {
    const res = await fetch(`${env.apiBaseUrl()}/api/admin/bookings/export${toQuery(params)}`, {
      cache: "no-store",
      headers: await authHeaders(),
    });
    if (!res.ok) throw new ApiError(res.status, `Export failed (${res.status})`);
    return res;
  },
};
