import { createHash, createHmac, timingSafeEqual } from "node:crypto";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { env } from "./env";

const COOKIE = "bt_admin";
const MAX_AGE_SECONDS = 60 * 60 * 12; // 12 hours

function sign(value: string) {
  return createHmac("sha256", env.sessionSecret()).update(value).digest("base64url");
}

function safeEqual(a: string, b: string) {
  // Hash first so lengths always match and nothing leaks through timing.
  const ha = createHash("sha256").update(a).digest();
  const hb = createHash("sha256").update(b).digest();
  return timingSafeEqual(ha, hb);
}

export function checkCredentials(username: string, password: string) {
  const userOk = safeEqual(username, env.adminUsername());
  const passOk = safeEqual(password, env.adminPassword());
  return userOk && passOk;
}

export async function createSession() {
  const expires = Math.floor(Date.now() / 1000) + MAX_AGE_SECONDS;
  const payload = `${env.adminUsername()}.${expires}`;
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

export async function isAuthenticated() {
  const raw = (await cookies()).get(COOKIE)?.value;
  if (!raw) return false;
  const i = raw.lastIndexOf(".");
  if (i < 0) return false;
  const payload = raw.slice(0, i);
  const sig = raw.slice(i + 1);
  if (!safeEqual(sig, sign(payload))) return false;
  const expires = Number(payload.split(".").pop());
  return Number.isFinite(expires) && expires > Date.now() / 1000;
}

/** Use at the top of every admin page, route handler and server action. */
export async function requireAdmin() {
  if (!(await isAuthenticated())) redirect("/admin/login");
}
