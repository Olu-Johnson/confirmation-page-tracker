import type { Metadata } from "next";
import { renderConfirmation } from "./render";

export const metadata: Metadata = { title: "Booking confirmed", robots: { index: false } };

/** Without a site key: the site (and its theme) is found from the booking's uid, else the default theme. */
export default async function BookingConfirmedPage({ searchParams }: PageProps<"/booking-confirmed">) {
  return renderConfirmation(await searchParams);
}
