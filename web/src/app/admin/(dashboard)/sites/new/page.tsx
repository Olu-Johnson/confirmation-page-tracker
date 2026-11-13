import type { Metadata } from "next";
import Link from "next/link";
import SiteForm from "../SiteForm";

export const metadata: Metadata = { title: "Add site" };

export default function NewSitePage() {
  return (
    <>
      <div className="page-head">
        <div>
          <Link className="crumb" href="/admin/sites">← Sites</Link>
          <h1>Add site</h1>
          <p>After saving you get the tracker script and Zylux webhook settings for this site.</p>
        </div>
      </div>
      <div className="card card-pad" style={{ maxWidth: 760 }}>
        <SiteForm />
      </div>
    </>
  );
}
