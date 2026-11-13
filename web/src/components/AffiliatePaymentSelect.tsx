"use client";

import { useFormStatus } from "react-dom";
import { setAffiliatePayment } from "@/app/admin/actions";
import type { AffiliatePaymentStatus } from "@/lib/types";

/** Super admin only: change a booking's affiliate payment; saves as soon as it changes. */
export default function AffiliatePaymentSelect({ id, status }: { id: string; status: AffiliatePaymentStatus }) {
  return (
    <form action={setAffiliatePayment} className="inline-form">
      <input type="hidden" name="id" value={id} />
      <Select status={status} />
    </form>
  );
}

function Select({ status }: { status: AffiliatePaymentStatus }) {
  const { pending } = useFormStatus();
  return (
    <select
      key={status}
      className={`select select-sm aff-${status.toLowerCase()}`}
      name="affiliatePaymentStatus"
      defaultValue={status}
      disabled={pending}
      aria-label="Affiliate payment"
      onChange={(e) => e.currentTarget.form?.requestSubmit()}
    >
      <option value="Pending">Pending</option>
      <option value="Paid">Paid</option>
      <option value="Rejected">Rejected</option>
    </select>
  );
}
