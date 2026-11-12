"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { api, ApiError } from "@/lib/api";
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
  if (!ID_RX.test(id)) return;
  let msg: string;
  try {
    const r = await api.sendConversion(id);
    msg = r.conversionStatus === "Sent" ? "sent" : `error:${r.lastConversionError ?? "TrackCrest did not accept it"}`;
  } catch (e) {
    msg = e instanceof ApiError ? `error:${e.message}` : "error:Could not send";
  }
  revalidatePath(`/admin/bookings/${id}`);
  redirect(`/admin/bookings/${id}?msg=${encodeURIComponent(msg)}`);
}
