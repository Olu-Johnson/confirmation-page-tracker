import { createHash, createHmac, timingSafeEqual } from "node:crypto";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { cache } from "react";
import { env } from "./env";

const COOKIE = "bt_admin";
const MAX_AGE_SECONDS = 60 * 60 * 12; // 12 hours

/**
 * Who is signed in.
 * - super: the ADMIN_USERNAME login. Sees every site and is the only one who can change anything.
 * - site:  a user created under a site (Admin → Sites → Users). Sees only that site's bookings, read-only.
 */
export type Session =
  | { role: "super"; name: string; exp: number }
  | { role: "site"; userId: string; email: string; name: string; siteId: string; siteName: string; exp: number };

export type NewSession =
  | { role: "super"; name: string }
  | { role: "site"; userId: string; email: string; name: string; siteId: string; siteName: string };

function sign(value: string) {
  return createHmac("sha256", env.sessionSecret()).update(value).digest("base64url");
}

function safeEqual(a: string, b: string) {
  // Hash first so lengths always match and nothing leaks through timing.
  const ha = createHash("sha256").update(a).digest();
  const hb = createHash("sha256").update(b).digest();
  return timingSafeEqual(ha, hb);
}

/** The super admin's username and password (from the environment). */
export function checkSuperAdmin(username: string, password: string) {
  const userOk = safeEqual(username, env.adminUsername());
  const passOk = safeEqual(password, env.adminPassword());
  return userOk && passOk;
}

export async function createSession(data: NewSession) {
  const exp = Math.floor(Date.now() / 1000) + MAX_AGE_SECONDS;
  const payload = Buffer.from(JSON.stringify({ ...data, exp })).toString("base64url");
  (await cookies()).set(COOKIE, `${payload}.${sign(payload)}`, {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.NODE_ENV === "production",
    path: "/",
    maxAge: MAX_AGE_SECONDS,
  });
}

export async function destroySession() {
  (await cookies()).delete(COOKIE);
}

function readCookie(raw: string | undefined): Session | null {
  if (!raw) return null;
  const i = raw.lastIndexOf(".");
  if (i < 0) return null;
  const payload = raw.slice(0, i);
  if (!safeEqual(raw.slice(i + 1), sign(payload))) return null;
  try {
    const s = JSON.parse(Buffer.from(payload, "base64url").toString()) as Session;
    if (!Number.isFinite(s.exp) || s.exp <= Date.now() / 1000) return null;
    if (s.role === "super") return s;
    if (s.role === "site" && /^[0-9a-f-]{36}$/i.test(s.siteId) && /^[0-9a-f-]{36}$/i.test(s.userId)) return s;
  } catch {}
  return null;
}

/**
 * The signed-in user, or null. A site user is checked with the API on every request,
 * so turning them off or deleting them signs them out straight away. Cached per request.
 */
export const getSession = cache(async (): Promise<Session | null> => {
  const s = readCookie((await cookies()).get(COOKIE)?.value);
  if (!s || s.role === "super") return s;
  try {
    const res = await fetch(`${env.apiBaseUrl()}/api/admin/site-users/${s.userId}/session`, {
      cache: "no-store",
      headers: { "X-Admin-Key": env.adminApiKey() },
    });
    if (!res.ok) return null;
    const live = (await res.json()) as { user: { siteId: string; name: string; email: string }; site: { name: string } };
    if (live.user.siteId !== s.siteId) return null; // moved to another site: sign in again
    return { ...s, name: live.user.name, email: live.user.email, siteName: live.site.name };
  } catch {
    return null;
  }
});

export async function isAuthenticated() {
  return (await getSession()) !== null;
}

/** Use at the top of every admin page, route handler and server action. Returns the signed-in user. */
export async function requireAdmin(): Promise<Session> {
  const s = await getSession();
  if (!s) redirect("/admin/login");
  return s;
}

/** For anything only the super admin may see or do (sites, setup, every change). */
export async function requireSuperAdmin() {
  const s = await requireAdmin();
  if (s.role !== "super") redirect("/admin");
  return s;
}
