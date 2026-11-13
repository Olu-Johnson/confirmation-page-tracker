import type { Metadata } from "next";
import { renderConfirmation } from "../render";

export const metadata: Metadata = { title: "Booking confirmed", robots: { index: false } };

/** A site's own confirmation page, in the theme chosen for it (Admin → Sites). Unknown keys get the default theme. */
export default async function SiteBookingConfirmedPage({ params, searchParams }: PageProps<"/booking-confirmed/[site]">) {
  const { site } = await params;
  return renderConfirmation(await searchParams, /^[a-z0-9][a-z0-9-]{1,49}$/i.test(site) ? site : "");
}
