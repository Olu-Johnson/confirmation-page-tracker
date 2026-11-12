// Server-side configuration. See .env.example for descriptions.
import { headers } from "next/headers";

/**
 * Public URL of this app: APP_PUBLIC_URL if set (recommended in production),
 * otherwise taken from the current request, so it follows whatever host/port you run on.
 */
export async function getAppUrl() {
  const fixed = process.env.APP_PUBLIC_URL?.trim();
  if (fixed) return fixed.replace(/\/+$/, "");
  const h = await headers();
  const host = h.get("x-forwarded-host") ?? h.get("host") ?? "localhost:3000";
  const proto = h.get("x-forwarded-proto") ?? (host.startsWith("localhost") || host.startsWith("127.") ? "http" : "https");
  return `${proto}://${host}`;
}

function required(name: string): string {
  const v = process.env[name];
  if (!v) throw new Error(`Missing environment variable ${name} (see web/.env.example)`);
  return v;
}

export const env = {
  apiBaseUrl: () => (process.env.API_BASE_URL?.trim() || "http://localhost:5145").replace(/\/+$/, ""),
  // Browser-facing API URL; leave empty to reuse API_BASE_URL (when the API has a single public address)
  apiPublicUrl: () =>
    (process.env.API_PUBLIC_URL?.trim() || process.env.API_BASE_URL?.trim() || "http://localhost:5145").replace(/\/+$/, ""),
  adminApiKey: () => required("ADMIN_API_KEY"),
  adminUsername: () => required("ADMIN_USERNAME"),
  adminPassword: () => required("ADMIN_PASSWORD"),
  sessionSecret: () => required("SESSION_SECRET"),
  brandName: () => process.env.BRAND_NAME ?? "Our team",
  brandSiteUrl: () => process.env.BRAND_SITE_URL?.trim() || "/",
  zyluxBaseUrl: () => (process.env.ZYLUX_BASE_URL ?? "https://meet.zyluxmedia.app").replace(/\/+$/, ""),
  zyluxBookingPath: () => process.env.ZYLUX_BOOKING_PATH ?? "",
  demoEnabled: () => process.env.NODE_ENV !== "production" || process.env.ENABLE_DEMO === "true",
};
