import { env } from "@/lib/env";
import { DEFAULT_THEME, getTheme } from "@/lib/themes";
import Confirmation from "./Confirmation";

const UID_RX = /^[A-Za-z0-9_-]{3,100}$/;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? "";

const STATUSES = ["accepted", "pending", "awaiting_payment", "cancelled", "rejected"];

interface LiveBooking { status: string; title?: string; startTime?: string; endTime?: string }

/**
 * The booking from Zylux Meet's public booking API (the uid is the key): its real status, so an unpaid
 * booking is never shown as confirmed and a paid one never as unpaid, and its real title and times
 * (Zylux's redirect doesn't include the end time). Null when Zylux can't be reached in time; the page
 * then falls back to the URL.
 */
async function liveBooking(uid: string): Promise<LiveBooking | null> {
  try {
    const res = await fetch(`${env.zyluxBaseUrl()}/api/bookings/${encodeURIComponent(uid)}`, {
      cache: "no-store",
      signal: AbortSignal.timeout(3000),
    });
    if (!res.ok) return null;
    const b = (await res.json())?.booking;
    if (typeof b?.status !== "string" || !STATUSES.includes(b.status)) return null;
    const str = (v: unknown) => (typeof v === "string" ? v : undefined);
    return { status: b.status, title: str(b.title), startTime: str(b.startTime), endTime: str(b.endTime) };
  } catch {
    return null;
  }
}

interface SiteLook { key: string; name: string; websiteUrl: string | null; theme: string }

/** The site's name, website and theme: by its key (from the URL), else by the booking. Null if unknown. */
async function siteLook(siteKey: string, uid: string): Promise<SiteLook | null> {
  if (!siteKey && !uid) return null;
  const q = new URLSearchParams();
  if (siteKey) q.set("site", siteKey);
  if (uid) q.set("uid", uid);
  try {
    const res = await fetch(`${env.apiBaseUrl()}/api/public/confirmation?${q}`, { cache: "no-store", signal: AbortSignal.timeout(2500) });
    return res.ok ? ((await res.json()) as SiteLook) : null;
  } catch {
    return null;
  }
}

type SP = Record<string, string | string[] | undefined>;

/**
 * Shared by /booking-confirmed (site found from the booking) and /booking-confirmed/{site-key}.
 * Reached via tracker.js after "scheduler:booked" / "scheduler:paid", or Zylux "Redirect on booking"
 * (?uid=…&startTime=…&title=…). Values in the URL are untrusted and only displayed as text.
 *
 * ?preview=1&theme=… shows a theme with a sample booking (used by the admin's theme picker).
 */
export async function renderConfirmation(sp: SP, siteKey = "") {
  const preview = one(sp.preview) === "1";
  const uid = UID_RX.test(one(sp.uid).trim()) ? one(sp.uid).trim() : "";
  const [live, site] = await Promise.all([uid ? liveBooking(uid) : null, siteLook(siteKey.toLowerCase(), uid)]);
  const status = live?.status ?? one(sp.status).trim();
  const theme = preview && one(sp.theme) ? getTheme(one(sp.theme)).key : getTheme(site?.theme ?? DEFAULT_THEME).key;

  // Preview without a real booking: tomorrow 10:00–10:30 in UTC as sample times.
  const sampleStart = new Date(); sampleStart.setUTCDate(sampleStart.getUTCDate() + 1); sampleStart.setUTCHours(10, 0, 0, 0);
  const sample = preview && !uid;

  return (
    <Confirmation
      uid={uid}
      title={(live?.title ?? (one(sp.title) || (sample ? "Sample consultation" : ""))).slice(0, 200)}
      startTime={live?.startTime ?? (one(sp.startTime) || (sample ? sampleStart.toISOString() : ""))}
      endTime={live?.endTime ?? (one(sp.endTime) || (sample ? new Date(sampleStart.getTime() + 30 * 60000).toISOString() : ""))}
      status={STATUSES.includes(status) ? status : "accepted"}
      brandName={site?.name || env.brandName()}
      brandSiteUrl={site?.websiteUrl || env.brandSiteUrl()}
      zyluxBaseUrl={env.zyluxBaseUrl()}
      theme={theme}
      preview={preview}
    />
  );
}
