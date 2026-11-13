import type { Metadata } from "next";
import Link from "next/link";
import { api, ApiError } from "@/lib/api";
import { fmtDateTime } from "@/lib/format";
import type { SiteStatus } from "@/lib/types";
import { ApiErrorBox, Badge } from "@/components/Badge";

export const metadata: Metadata = { title: "Sites" };

const MODE_LABEL = { Lead: "Leads", Sale: "Sales", Both: "Leads + sales" } as const;

export default async function SitesPage() {
  let sites: SiteStatus[] = [];
  let error = "";
  try { sites = (await api.config()).sites; } catch (e) { error = e instanceof ApiError ? e.message : "Unexpected error"; }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Sites</h1>
          <p>Each client website using Zylux Meet: what it reports to TrackCrest and where.</p>
        </div>
        <Link className="btn btn-primary" href="/admin/sites/new">Add site</Link>
      </div>

      {error && <ApiErrorBox message={error} />}

      <div className="card table-wrap">
        <table className="data">
          <thead>
            <tr><th>Site</th><th>Reports</th><th>Postbacks</th><th>Website</th><th>Last webhook</th><th>Last tracked booking</th></tr>
          </thead>
          <tbody>
            {sites.map((s) => {
              const missing = [
                s.mode !== "Sale" && !s.leadPostbackConfigured ? "lead URL" : null,
                s.mode !== "Lead" && !s.salePostbackConfigured ? "sale URL" : null,
                !s.webhookSecretConfigured ? "webhook secret" : null,
              ].filter(Boolean);
              return (
                <tr key={s.id}>
                  <td>
                    <Link className="row-link" href={`/admin/sites/${s.id}`}>
                      <div className="cell-main">{s.name}</div>
                      <div className="cell-sub mono">{s.key}</div>
                    </Link>
                  </td>
                  <td><div className="badges"><Badge tone="info">{MODE_LABEL[s.mode]}</Badge>{!s.active && <Badge tone="muted">Off</Badge>}</div></td>
                  <td>{missing.length ? <Badge tone="bad">Missing {missing.join(", ")}</Badge> : <Badge tone="ok">Ready</Badge>}</td>
                  <td className="mono">{s.allowedOrigins.join(", ") || "—"}</td>
                  <td>{fmtDateTime(s.lastWebhookAt)}</td>
                  <td>{fmtDateTime(s.lastTrackedAt)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {!error && sites.length === 0 && <div className="empty">No sites yet. Add the first client website to start tracking its bookings.</div>}
      </div>
    </>
  );
}
