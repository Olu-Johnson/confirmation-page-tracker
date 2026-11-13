"use server";

import { refresh, revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import type { AffiliatePaymentStatus, SiteInput } from "@/lib/types";
import { checkSuperAdmin, createSession, destroySession, requireSuperAdmin } from "@/lib/session";

/** Signs in the super admin (ADMIN_USERNAME) or a site user (email + password stored in the API). */
export async function login(_prev: { error?: string } | undefined, form: FormData) {
  const username = String(form.get("username") ?? "").trim();
  const password = String(form.get("password") ?? "");
  if (checkSuperAdmin(username, password)) {
    await createSession({ role: "super", name: username });
  } else {
    try {
      const { user, site } = await api.siteLogin(username, password);
      await createSession({ role: "site", userId: user.id, email: user.email, name: user.name, siteId: site.id, siteName: site.name });
    } catch (e) {
      if (e instanceof ApiError && e.status !== 401 && e.status !== 400) return { error: e.message };
      await new Promise((r) => setTimeout(r, 600)); // slow down guessing
      return { error: "Incorrect username or password." };
    }
  }
  const next = String(form.get("next") ?? "");
  redirect(next.startsWith("/admin") && !next.startsWith("//") ? next : "/admin");
}

export async function logout() {
  await destroySession();
  redirect("/admin/login");
}

const ID_RX = /^[0-9a-f-]{36}$/i;

export async function updateReconciliation(form: FormData) {
  await requireSuperAdmin();
  const id = String(form.get("id") ?? "");
  if (!ID_RX.test(id)) return;
  let msg = "saved";
  try {
    await api.updateReconciliation(id, {
      status: String(form.get("status") ?? "Unreviewed"),
      notes: String(form.get("notes") ?? ""),
    });
  } catch (e) {
    msg = e instanceof ApiError ? `error:${e.message}` : "error:Could not save";
  }
  revalidatePath(`/admin/bookings/${id}`);
  redirect(`/admin/bookings/${id}?msg=${encodeURIComponent(msg)}`);
}

export async function sendConversion(form: FormData) {
  await requireSuperAdmin();
  const id = String(form.get("id") ?? "");
  const kind = String(form.get("kind") ?? "");
  if (!ID_RX.test(id) || (kind !== "Lead" && kind !== "Sale")) return;
  let msg: string;
  try {
    const r = await api.sendConversion(id, kind);
    msg = r.status === "Sent" ? `sent:${kind}` : `error:${r.error ?? "TrackCrest did not accept it"}`;
  } catch (e) {
    msg = e instanceof ApiError ? `error:${e.message}` : "error:Could not send";
  }
  revalidatePath(`/admin/bookings/${id}`);
  redirect(`/admin/bookings/${id}?msg=${encodeURIComponent(msg)}`);
}

/** Creates or updates a site from the site form. Errors come back to the form; success goes to the site page. */
export async function saveSite(_prev: { error?: string } | undefined, form: FormData): Promise<{ error?: string }> {
  await requireSuperAdmin();
  const id = String(form.get("id") ?? "");
  if (id && !ID_RX.test(id)) return { error: "Unknown site" };
  const mode = String(form.get("mode") ?? "Both");
  const body: SiteInput = {
    key: String(form.get("key") ?? ""),
    name: String(form.get("name") ?? ""),
    mode: mode === "Lead" || mode === "Sale" ? mode : "Both",
    leadPostbackUrl: String(form.get("leadPostbackUrl") ?? ""),
    salePostbackUrl: String(form.get("salePostbackUrl") ?? ""),
    allowedOrigins: String(form.get("allowedOrigins") ?? "").split(/[\s,]+/).filter(Boolean),
    active: form.get("active") === "on",
  };
  const secret = String(form.get("webhookSecret") ?? "").trim();
  if (secret) body.webhookSecret = secret;
  let saved;
  try {
    saved = id ? await api.updateSite(id, body) : await api.createSite(body);
  } catch (e) {
    return { error: e instanceof ApiError ? e.message : "Could not save the site" };
  }
  revalidatePath("/admin/sites");
  redirect(`/admin/sites/${saved.id}?msg=saved`);
}

export async function regenerateSiteSecret(form: FormData) {
  await requireSuperAdmin();
  const id = String(form.get("id") ?? "");
  if (!ID_RX.test(id)) return;
  let msg = "secret";
  try {
    await api.updateSite(id, { regenerateSecret: true });
  } catch (e) {
    msg = e instanceof ApiError ? `error:${e.message}` : "error:Could not change the secret";
  }
  revalidatePath(`/admin/sites/${id}`);
  redirect(`/admin/sites/${id}?msg=${encodeURIComponent(msg)}`);
}

const AFFILIATE_STATUSES: AffiliatePaymentStatus[] = ["Pending", "Paid", "Rejected"];

/** Super admin only. Stays on the current page (bookings list or booking page). */
export async function setAffiliatePayment(form: FormData) {
  await requireSuperAdmin();
  const id = String(form.get("id") ?? "");
  const status = String(form.get("affiliatePaymentStatus") ?? "") as AffiliatePaymentStatus;
  if (!ID_RX.test(id) || !AFFILIATE_STATUSES.includes(status)) return;
  await api.setAffiliatePayment(id, status);
  refresh();
}

// ---- Site users ----

function userMsg(siteId: string, msg: string): never {
  revalidatePath(`/admin/sites/${siteId}`);
  redirect(`/admin/sites/${siteId}?msg=${encodeURIComponent(msg)}#users`);
}

function errMsg(e: unknown, fallback: string) {
  return `error:${e instanceof ApiError ? e.message : fallback}`;
}

export async function createSiteUser(form: FormData) {
  await requireSuperAdmin();
  const siteId = String(form.get("siteId") ?? "");
  if (!ID_RX.test(siteId)) return;
  let msg = "user-added";
  try {
    await api.createSiteUser(siteId, {
      name: String(form.get("name") ?? ""),
      email: String(form.get("email") ?? ""),
      password: String(form.get("password") ?? ""),
    });
  } catch (e) {
    msg = errMsg(e, "Could not add the user");
  }
  userMsg(siteId, msg);
}

/** Turns a user on/off, or sets a new password, depending on the button pressed. */
export async function updateSiteUser(form: FormData) {
  await requireSuperAdmin();
  const siteId = String(form.get("siteId") ?? "");
  const id = String(form.get("id") ?? "");
  if (!ID_RX.test(siteId) || !ID_RX.test(id)) return;
  const op = String(form.get("op") ?? "");
  let msg = "user-saved";
  try {
    if (op === "activate" || op === "deactivate") await api.updateSiteUser(id, { active: op === "activate" });
    else if (op === "password") {
      await api.updateSiteUser(id, { password: String(form.get("password") ?? "") });
      msg = "user-password";
    } else return;
  } catch (e) {
    msg = errMsg(e, "Could not update the user");
  }
  userMsg(siteId, msg);
}

export async function deleteSiteUser(form: FormData) {
  await requireSuperAdmin();
  const siteId = String(form.get("siteId") ?? "");
  const id = String(form.get("id") ?? "");
  if (!ID_RX.test(siteId) || !ID_RX.test(id)) return;
  let msg = "user-deleted";
  try {
    await api.deleteSiteUser(id);
  } catch (e) {
    msg = errMsg(e, "Could not delete the user");
  }
  userMsg(siteId, msg);
}
