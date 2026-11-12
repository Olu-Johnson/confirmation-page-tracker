"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/admin", label: "Bookings", match: (p: string) => p === "/admin" || p.startsWith("/admin/bookings") },
  { href: "/admin/setup", label: "Setup", match: (p: string) => p.startsWith("/admin/setup") },
];

export default function NavLinks() {
  const path = usePathname();
  return (
    <nav className="nav" aria-label="Admin">
      {links.map((l) => (
        <Link key={l.href} href={l.href} aria-current={l.match(path) ? "page" : undefined}>{l.label}</Link>
      ))}
    </nav>
  );
}
