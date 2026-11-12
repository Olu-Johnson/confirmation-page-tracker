import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { isAuthenticated } from "@/lib/session";
import LoginForm from "./LoginForm";

export const metadata: Metadata = { title: "Sign in" };

export default async function LoginPage({ searchParams }: PageProps<"/admin/login">) {
  if (await isAuthenticated()) redirect("/admin");
  const next = (await searchParams).next;
  return (
    <div className="login-wrap">
      <div className="card login">
        <div className="brand" style={{ marginBottom: 18 }}>
          <span className="brand-mark">BT</span> Booking Tracker
        </div>
        <h1>Sign in</h1>
        <p>Tracked bookings and TrackCrest conversions.</p>
        <LoginForm next={typeof next === "string" ? next : ""} />
      </div>
    </div>
  );
}
