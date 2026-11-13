"use client";

import { useEffect, useRef, useSyncExternalStore } from "react";
import styles from "./confirm.module.css";

interface Props {
  uid: string;
  title: string;
  startTime: string;
  endTime: string;
  status: string;
  brandName: string;
  brandSiteUrl: string;
  zyluxBaseUrl: string;
}

const DEFAULT_MINUTES = 60;
const noopSubscribe = () => () => {};

function parse(s: string) {
  if (!s) return null;
  const d = new Date(s);
  return isNaN(d.getTime()) ? null : d;
}
const stamp = (d: Date) => d.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}/, "");
const icsEscape = (s: string) => s.replace(/[\\;,]/g, "\\$&").replace(/\n/g, "\\n");

export default function Confirmation(p: Props) {
  const start = parse(p.startTime);
  const end = parse(p.endTime) ?? (start ? new Date(start.getTime() + DEFAULT_MINUTES * 60000) : null);
  const title = p.title || "Your consultation";
  const manageUrl = p.uid ? `${p.zyluxBaseUrl}/booking/${encodeURIComponent(p.uid)}` : "";
  const joinUrl = p.uid ? `${p.zyluxBaseUrl}/join/${encodeURIComponent(p.uid)}` : "";

  // Times depend on the visitor's time zone, so format them only in the browser (after hydration).
  const isClient = useSyncExternalStore(noopSubscribe, () => true, () => false);
  const when = isClient && start && end
    ? {
        date: start.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric", year: "numeric" }),
        time: `${start.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })} – ${end.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })}`,
        zone: Intl.DateTimeFormat().resolvedOptions().timeZone.replace(/_/g, " "),
      }
    : null;

  let calendar: { google: string; outlook: string; ics: string } | null = null;
  if (start && end) {
    const details = [joinUrl && `Join: ${joinUrl}`, manageUrl && `Reschedule or cancel: ${manageUrl}`].filter(Boolean).join("\n");
    const ics = [
      "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Booking Tracker//EN", "BEGIN:VEVENT",
      `UID:${p.uid || stamp(start)}@booking`, `DTSTAMP:${stamp(start)}`,
      `DTSTART:${stamp(start)}`, `DTEND:${stamp(end)}`,
      `SUMMARY:${icsEscape(title)}`, `DESCRIPTION:${icsEscape(details)}`, "END:VEVENT", "END:VCALENDAR",
    ].join("\r\n");
    calendar = {
      google: `https://calendar.google.com/calendar/render?action=TEMPLATE&text=${encodeURIComponent(title)}&dates=${stamp(start)}/${stamp(end)}&details=${encodeURIComponent(details)}`,
      outlook: `https://outlook.live.com/calendar/0/deeplink/compose?path=/calendar/action/compose&rru=addevent&subject=${encodeURIComponent(title)}&startdt=${start.toISOString()}&enddt=${end.toISOString()}&body=${encodeURIComponent(details)}`,
      ics: `data:text/calendar;charset=utf-8,${encodeURIComponent(ics)}`,
    };
  }

  const variant =
    p.status === "awaiting_payment"
      ? { tone: styles.toneWarn, eyebrow: "One more step", heading: "Complete your payment", lead: "Your slot is held, but the booking isn't confirmed until it's paid. Pay now to secure it — you'll get a confirmation email once it's paid." }
      : p.status === "cancelled" || p.status === "rejected"
        ? { tone: styles.toneWarn, eyebrow: "Not booked", heading: "This booking isn't active", lead: `This booking was ${p.status}. You can book a new time from ${p.brandName}'s website.` }
      : p.status === "pending"
        ? { tone: styles.toneWarn, eyebrow: "Request received", heading: "Almost there!", lead: "Your request has been sent. You'll get an email as soon as it's confirmed." }
        : { tone: styles.toneOk, eyebrow: "Booking confirmed", heading: "You're booked in!", lead: "A confirmation email with the meeting details and a calendar invite is on its way to your inbox." };

  const confirmed = p.status === "accepted";
  useConfetti(confirmed);

  return (
    <div className={styles.page}>
      <canvas className={styles.confetti} id="confetti" aria-hidden="true" />
      <main className={styles.card}>
        <div className={`${styles.badge} ${variant.tone}`} aria-hidden="true">
          {confirmed ? (
            <svg viewBox="0 0 24 24" fill="none"><path className={styles.tick} d="M5 12.5l4.5 4.5L19 7.5" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" /></svg>
          ) : (
            <svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="8" stroke="currentColor" strokeWidth="2.5" /><path d="M12 8v4.5l3 2" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" /></svg>
          )}
        </div>

        <p className={styles.eyebrow}>{variant.eyebrow}</p>
        <h1 className={styles.h1}>{variant.heading}</h1>
        <p className={styles.lead}>{variant.lead}</p>

        <section className={styles.details} aria-label="Booking details">
          <div className={styles.row}><span className={styles.k}>What</span><span className={styles.v}>{title}</span></div>
          {start && (
            <div className={styles.row}>
              <span className={styles.k}>When</span>
              <span className={styles.v}>
                {when ? <>{when.date}<br />{when.time}<small>Shown in your time zone: {when.zone}</small></> : " "}
              </span>
            </div>
          )}
          {p.uid && (
            <div className={styles.row}><span className={styles.k}>Reference</span><span className={`${styles.v} ${styles.mono}`}>{p.uid}</span></div>
          )}
        </section>

        {calendar && p.status !== "awaiting_payment" && (
          <div className={styles.cal}>
            <a className={styles.chip} href={calendar.google} target="_blank" rel="noopener noreferrer">Google Calendar</a>
            <a className={styles.chip} href={calendar.outlook} target="_blank" rel="noopener noreferrer">Outlook</a>
            <a className={styles.chip} href={calendar.ics} download="booking.ics">Apple / .ics</a>
          </div>
        )}

        {p.status !== "cancelled" && p.status !== "rejected" && <div className={styles.next}>
          <h2>What happens next</h2>
          <ul>
            {p.status === "awaiting_payment" && <li>Complete the payment to secure your slot.</li>}
            <li>Check your inbox (and spam folder) for the confirmation email.</li>
            <li>You&apos;ll get a reminder before the meeting.</li>
            <li>Your meeting link opens 5 minutes before the start time.</li>
          </ul>
        </div>}

        <div className={styles.actions}>
          {manageUrl && p.status === "awaiting_payment" && <a className={`${styles.btn} ${styles.primary}`} href={manageUrl}>Pay now</a>}
          {joinUrl && confirmed && <a className={`${styles.btn} ${styles.primary}`} href={joinUrl} target="_blank" rel="noopener noreferrer">Meeting link</a>}
          {manageUrl && p.status !== "cancelled" && p.status !== "rejected" && <a className={`${styles.btn} ${styles.secondary}`} href={manageUrl} target="_blank" rel="noopener noreferrer">Reschedule or cancel</a>}
          <a className={`${styles.btn} ${styles.secondary}`} href={p.brandSiteUrl}>Back to {p.brandName}</a>
        </div>
      </main>
    </div>
  );
}

function useConfetti(enabled: boolean) {
  const ran = useRef(false);
  useEffect(() => {
    if (!enabled || ran.current) return;
    ran.current = true;
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
    const c = document.getElementById("confetti") as HTMLCanvasElement | null;
    const ctx = c?.getContext("2d");
    if (!c || !ctx) return;
    c.width = window.innerWidth;
    c.height = window.innerHeight;
    const colors = ["#5b3fd6", "#f59e0b", "#10b981", "#ec4899", "#3b82f6", "#facc15"];
    const pieces = Array.from({ length: 140 }, (_, i) => ({
      x: Math.random() * c.width, y: -20 - Math.random() * c.height * 0.5,
      w: 6 + Math.random() * 6, h: 10 + Math.random() * 8, color: colors[i % colors.length],
      vy: 2 + Math.random() * 3, vx: -1.5 + Math.random() * 3, rot: Math.random() * Math.PI, vr: -0.15 + Math.random() * 0.3,
    }));
    let raf = 0;
    const tick = () => {
      ctx.clearRect(0, 0, c.width, c.height);
      for (let i = pieces.length - 1; i >= 0; i--) {
        const q = pieces[i];
        q.x += q.vx; q.y += q.vy; q.rot += q.vr;
        ctx.save(); ctx.translate(q.x, q.y); ctx.rotate(q.rot);
        ctx.fillStyle = q.color; ctx.fillRect(-q.w / 2, -q.h / 2, q.w, q.h); ctx.restore();
        if (q.y > c.height + 30) pieces.splice(i, 1);
      }
      if (pieces.length) raf = requestAnimationFrame(tick);
    };
    const t = setTimeout(() => { raf = requestAnimationFrame(tick); }, 300);
    return () => { clearTimeout(t); cancelAnimationFrame(raf); };
  }, [enabled]);
}
