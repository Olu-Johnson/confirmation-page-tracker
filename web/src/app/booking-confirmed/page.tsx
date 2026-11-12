import type { Metadata } from "next";
import { env } from "@/lib/env";
import Confirmation from "./Confirmation";

export const metadata: Metadata = { title: "Booking confirmed" };

const UID_RX = /^[A-Za-z0-9_-]{3,100}$/;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? "";

/**
 * Reached via tracker.js after "scheduler:booked", or Zylux "Redirect on booking"
 * (?uid=…&startTime=…&title=…). Values in the URL are untrusted and only displayed as text.
 */
export default async function BookingConfirmedPage({ searchParams }: PageProps<"/booking-confirmed">) {
  const sp = await searchParams;
  const uid = one(sp.uid).trim();
  const status = one(sp.status).trim();

  return (
    <Confirmation
      uid={UID_RX.test(uid) ? uid : ""}
      title={one(sp.title).slice(0, 200)}
      startTime={one(sp.startTime)}
      endTime={one(sp.endTime)}
      status={["accepted", "pending", "awaiting_payment"].includes(status) ? status : "accepted"}
      brandName={env.brandName()}
      brandSiteUrl={env.brandSiteUrl()}
      zyluxBaseUrl={env.zyluxBaseUrl()}
    />
  );
}
