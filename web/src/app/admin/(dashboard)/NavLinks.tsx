"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/admin", label: "Bookings", match: (p: string) => p === "/admin" || p.startsWith("/admin/bookings"), superOnly: false },
  { href: "/admin/sites", label: "Sites", match: (p: string) => p.startsWith("/admin/sites"), superOnly: true },
  { href: "/admin/setup", label: "Setup", match: (p: string) => p.startsWith("/admin/setup"), superOnly: true },
];

export default function NavLinks({ superAdmin }: { superAdmin: boolean }) {
  const path = usePathname();
  return (
    <nav className="nav" aria-label="Admin">
      {links.filter((l) => superAdmin || !l.superOnly).map((l) => (
        <Link key={l.href} href={l.href} aria-current={l.match(path) ? "page" : undefined}>{l.label}</Link>
      ))}
    </nav>
  );
}
