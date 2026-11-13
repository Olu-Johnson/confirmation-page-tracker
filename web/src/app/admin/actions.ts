"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import type { SiteInput } from "@/lib/types";
import { checkCredentials, createSession, destroySession, requireAdmin } from "@/lib/session";

export async function login(_prev: { error?: string } | undefined, form: FormData) {
  const username = String(form.get("username") ?? "");
  const password = String(form.get("password") ?? "");
  if (!checkCredentials(username, password)) {
    await new Promise((r) => setTimeout(r, 600)); // slow down guessing
    return { error: "Incorrect username or password." };
  }
  await createSession();
  const next = String(form.get("next") ?? "");
  redirect(next.startsWith("/admin") && !next.startsWith("//") ? next : "/admin");
}

export async function logout() {
  await destroySession();
  redirect("/admin/login");
}

const ID_RX = /^[0-9a-f-]{36}$/i;

export async function updateReconciliation(form: FormData) {
  await requireAdmin();
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
  await requireAdmin();
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
  await requireAdmin();
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
  await requireAdmin();
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
