import type { Metadata } from "next";
import Link from "next/link";
import { api, ApiError, toQuery } from "@/lib/api";
import { adminTimeZone, bookingStatusMeta, conversionStatusMeta, fmtDateTime, fmtMoney, reconciliationMeta } from "@/lib/format";
import { FILTER_KEYS, type BookingPage, type Site, type Stats } from "@/lib/types";
import { ApiErrorBox, Badge, BookingBadge, ConversionBadge, ReconciliationBadge } from "@/components/Badge";

export const metadata: Metadata = { title: "Bookings" };

export default async function BookingsPage({ searchParams }: PageProps<"/admin">) {
  const sp = await searchParams;
  const filters: Record<string, string | undefined> = {};
  for (const k of FILTER_KEYS) {
    const v = sp[k];
    filters[k] = typeof v === "string" && v.trim() ? v.trim() : undefined;
  }
  const page = Math.max(1, Number(sp.page) || 1);
  const pageSize = 25;

  let data: BookingPage | null = null;
  let stats: Stats | null = null;
  let sites: Site[] = [];
  let error = "";
  try {
    [data, stats, sites] = await Promise.all([
      api.bookings({ ...filters, page: String(page), pageSize: String(pageSize) }),
      api.stats(filters),
      api.sites(),
    ]);
  } catch (e) {
    error = e instanceof ApiError ? e.message : "Unexpected error";
  }

  const hasFilters = Object.values(filters).some(Boolean);
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const pageHref = (p: number) => `/admin${toQuery({ ...filters, page: p > 1 ? String(p) : undefined })}`;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Tracked bookings</h1>
          <p>Only bookings made through a TrackCrest tracking link appear here.</p>
        </div>
        <a className="btn" href={`/admin/export${toQuery(filters)}`}>Export CSV</a>
      </div>

      {error && <ApiErrorBox message={error} />}

      {stats && <StatCards stats={stats} />}

      <h2 className="section-title">Bookings</h2>
      <div className="card">
        <form className="filters" method="get" action="/admin">
          <div className="field wide">
            <label className="label" htmlFor="search">Search</label>
            <input className="input" id="search" name="search" placeholder="Name, email, booking ID, click ID, source…" defaultValue={filters.search} />
          </div>
          {sites.length > 1 && <Select name="site" label="Site" value={filters.site} options={sites.map((x) => [x.id, x.name])} />}
          <Select name="status" label="Booking" value={filters.status} options={Object.entries(bookingStatusMeta).map(([k, m]) => [k, m.label])} />
          <Select name="conversion" label="Conversion" value={filters.conversion} options={Object.entries(conversionStatusMeta).map(([k, m]) => [k, m.label])} />
          <Select name="reconciliation" label="Review" value={filters.reconciliation} options={Object.entries(reconciliationMeta).map(([k, m]) => [k, m.label])} />
          <Select name="verified" label="Zylux webhook" value={filters.verified} options={[["yes", "Verified"], ["no", "Unverified"]]} />
          <div className="field">
            <span className="label">Tracked between</span>
            <div className="dates">
              <input className="input" type="date" name="from" aria-label="From date" defaultValue={filters.from} />
              <input className="input" type="date" name="to" aria-label="To date" defaultValue={filters.to} />
            </div>
          </div>
          <div className="filters-actions">
            <button className="btn btn-primary" type="submit">Apply</button>
            {hasFilters && <Link className="btn" href="/admin">Clear</Link>}
          </div>
        </form>

        <div className="table-wrap">
          <table className="data">
            <thead>
              <tr>
                <th>Tracked</th>
                <th>Booking</th>
                <th>Attendee</th>
                <th>Meeting</th>
                <th>Source</th>
                <th className="num">Amount</th>
                <th>Status</th>
                <th>TrackCrest</th>
                <th>Review</th>
              </tr>
            </thead>
            <tbody>
              {data?.items.map((b) => {
                const href = `/admin/bookings/${b.id}`;
                return (
                  <tr key={b.id}>
                    <td><Link className="row-link" href={href}>{fmtDateTime(b.createdAt)}</Link></td>
                    <td>
                      <Link className="row-link" href={href}>
                        <div className="cell-main">{b.title ?? "Untitled booking"}</div>
                        <div className="cell-sub mono">{b.bookingUid}</div>
                      </Link>
                    </td>
                    <td>
                      <div>{b.attendeeName ?? "—"}</div>
                      <div className="cell-sub">{b.attendeeEmail ?? ""}</div>
                    </td>
                    <td>{fmtDateTime(b.startTime)}</td>
                    <td>
                      <div>{b.affiliateId ?? b.sub1 ?? "—"}</div>
                      <div className="cell-sub mono" title="TrackCrest click ID">{b.clickId}</div>
                    </td>
                    <td className="num">{fmtMoney(b.amount, b.currency)}</td>
                    <td>
                      <div className="badges">
                        <BookingBadge status={b.status} />
                        {!b.webhookConfirmed && <Badge tone="warn" title="No signed Zylux webhook received yet">Unverified</Badge>}
                      </div>
                    </td>
                    <td><ConversionBadge status={b.conversionStatus} /></td>
                    <td><ReconciliationBadge status={b.reconciliationStatus} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {data && data.items.length === 0 && (
            <div className="empty">
              {hasFilters ? "No bookings match these filters." : "No tracked bookings yet. They appear here when someone books after arriving through a TrackCrest link."}
            </div>
          )}
        </div>

        {data && data.total > 0 && (
          <div className="pager">
            <span>
              {(data.page - 1) * data.pageSize + 1}–{Math.min(data.page * data.pageSize, data.total)} of {data.total} · times in {adminTimeZone}
            </span>
            <div className="btns">
              {page > 1 ? <Link className="btn btn-sm" href={pageHref(page - 1)}>Previous</Link> : <span className="btn btn-sm" aria-disabled="true" style={{ opacity: 0.5 }}>Previous</span>}
              {page < pages ? <Link className="btn btn-sm" href={pageHref(page + 1)}>Next</Link> : <span className="btn btn-sm" aria-disabled="true" style={{ opacity: 0.5 }}>Next</span>}
            </div>
          </div>
        )}
      </div>

      {stats && stats.sources.length > 0 && (
        <>
          <h2 className="section-title">By source</h2>
          <div className="card table-wrap">
            <table className="data">
              <thead>
                <tr><th>Affiliate / sub1</th><th className="num">Bookings</th><th className="num">Paid</th><th className="num">Booked with a postback sent</th></tr>
              </thead>
              <tbody>
                {stats.sources.map((s) => (
                  <tr key={s.source}>
                    <td><Link href={`/admin${toQuery({ ...filters, search: s.source === "(none)" ? undefined : s.source, page: undefined })}`}>{s.source}</Link></td>
                    <td className="num">{s.bookings}</td>
                    <td className="num">{s.paid}</td>
                    <td className="num">{s.conversionsSent}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </>
  );
}

function StatCards({ stats }: { stats: Stats }) {
  const paid = stats.byStatus.Paid ?? 0;
  const leadsSent = stats.leads.Sent ?? 0;
  const salesSent = stats.sales.Sent ?? 0;
  const failed = (stats.leads.Failed ?? 0) + (stats.sales.Failed ?? 0);
  const queued = (stats.leads.Queued ?? 0) + (stats.sales.Queued ?? 0);
  const waiting = (stats.leads.Waiting ?? 0) + (stats.sales.Waiting ?? 0);
  const unreviewed = stats.byReconciliation.Unreviewed ?? 0;
  const disputed = stats.byReconciliation.Disputed ?? 0;
  const revenue = stats.revenue.length ? stats.revenue.map((r) => fmtMoney(r.amount, r.currency)).join(" + ") : fmtMoney(0, "CAD");

  return (
    <div className="stats">
      <div className="card stat"><div className="k">Tracked bookings</div><div className="v">{stats.total}</div><div className="s">{stats.unverified} not yet verified by Zylux</div></div>
      <div className="card stat"><div className="k">Paid</div><div className="v">{paid}</div><div className="s">{revenue}</div></div>
      <div className="card stat"><div className="k">Leads sent</div><div className="v">{leadsSent}</div><div className="s">{queued} queued · {waiting} waiting</div></div>
      <div className="card stat"><div className="k">Sales sent</div><div className="v">{salesSent}</div><div className="s">Paid meetings reported</div></div>
      <div className={`card stat ${failed ? "bad" : ""}`}><div className="k">Failed postbacks</div><div className="v">{failed}</div><div className="s">Retry from the booking page</div></div>
      <div className={`card stat ${disputed ? "warn" : ""}`}><div className="k">To review</div><div className="v">{unreviewed}</div><div className="s">{disputed} disputed</div></div>
      {stats.cancelledAfterSent > 0 && (
        <div className="card stat warn"><div className="k">Cancelled after payout</div><div className="v">{stats.cancelledAfterSent}</div><div className="s">Reverse these in TrackCrest</div></div>
      )}
    </div>
  );
}

function Select({ name, label, value, options }: { name: string; label: string; value?: string; options: string[][] }) {
  return (
    <div className="field">
      <label className="label" htmlFor={name}>{label}</label>
      <select className="select" id={name} name={name} defaultValue={value ?? ""}>
        <option value="">All</option>
        {options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
      </select>
    </div>
  );
}
