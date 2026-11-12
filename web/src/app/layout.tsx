import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: "Booking Tracker", template: "%s · Booking Tracker" },
  description: "Tracked Zylux Meet bookings and TrackCrest conversions",
  robots: { index: false, follow: false },
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
