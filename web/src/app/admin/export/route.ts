import { api, ApiError } from "@/lib/api";
import { isAuthenticated } from "@/lib/session";
import { FILTER_KEYS } from "@/lib/types";

export async function GET(req: Request) {
  if (!(await isAuthenticated())) return new Response("Unauthorized", { status: 401 });

  const sp = new URL(req.url).searchParams;
  const filters: Record<string, string | undefined> = {};
  for (const k of FILTER_KEYS) filters[k] = sp.get(k) || undefined;

  try {
    const res = await api.exportCsv(filters);
    return new Response(res.body, {
      headers: {
        "Content-Type": "text/csv; charset=utf-8",
        "Content-Disposition": res.headers.get("Content-Disposition") ?? 'attachment; filename="tracked-bookings.csv"',
        "Cache-Control": "no-store",
      },
    });
  } catch (e) {
    return new Response(e instanceof ApiError ? e.message : "Export failed", { status: 502 });
  }
}
