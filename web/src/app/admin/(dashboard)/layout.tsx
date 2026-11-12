import Link from "next/link";
import { requireAdmin } from "@/lib/session";
import { logout } from "../actions";
import NavLinks from "./NavLinks";

export default async function DashboardLayout({ children }: LayoutProps<"/admin">) {
  await requireAdmin();
  return (
    <div className="shell">
      <header className="topbar">
        <div className="topbar-inner">
          <Link href="/admin" className="brand"><span className="brand-mark">BT</span> Booking Tracker</Link>
          <NavLinks />
          <form action={logout}>
            <button className="btn btn-sm" type="submit">Sign out</button>
          </form>
        </div>
      </header>
      <main className="main">{children}</main>
    </div>
  );
}
