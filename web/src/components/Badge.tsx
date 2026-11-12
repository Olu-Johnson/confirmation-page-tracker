import { bookingStatusMeta, conversionStatusMeta, reconciliationMeta } from "@/lib/format";
import type { BookingStatus, ConversionStatus, ReconciliationStatus } from "@/lib/types";

export function Badge({ tone, children, title }: { tone: string; children: React.ReactNode; title?: string }) {
  return <span className={`badge tone-${tone}`} title={title}>{children}</span>;
}

export function BookingBadge({ status }: { status: BookingStatus }) {
  const m = bookingStatusMeta[status] ?? { label: status, tone: "muted" };
  return <Badge tone={m.tone}>{m.label}</Badge>;
}

export function ConversionBadge({ status }: { status: ConversionStatus }) {
  const m = conversionStatusMeta[status] ?? { label: status, tone: "muted", help: "" };
  return <Badge tone={m.tone} title={m.help}>{m.label}</Badge>;
}

export function ReconciliationBadge({ status }: { status: ReconciliationStatus }) {
  const m = reconciliationMeta[status] ?? { label: status, tone: "muted" };
  return <Badge tone={m.tone}>{m.label}</Badge>;
}

export function ApiErrorBox({ message }: { message: string }) {
  return (
    <div className="alert tone-bad" role="alert">
      <strong>Couldn&apos;t load data from the API.</strong> {message}
    </div>
  );
}
