# Booking Tracker — Zylux Meet × TrackCrest

Records Zylux Meet bookings that come through a TrackCrest tracking link, reports them to TrackCrest
server-to-server as **leads** (meeting booked) and/or **sales** (paid meeting paid), and gives an admin
for reconciliation with the business owner. Several client sites can use it, each with its own settings.

```
Affiliate link → TrackCrest → https://dept.habyyer.com/?click_id=…   (or the Zylux booking link ?click_id=…)
   Zylux embed.js (data-tracking="on") passes click_id into the booking; Zylux keeps it on it (booking.tracking)
   tracker.js also saves it on the client's site and reports "scheduler:booked" → POST /api/track/booking
Zylux webhooks (HMAC-signed, payload.tracking.click_id) → POST /api/webhooks/zylux/{site-key}
   tracked from the webhook alone, so a visitor who closes the tab after paying is still counted
   site mode Lead / Sale / Both → lead when booked, sale when paid → that site's TrackCrest postback (retries)
Admin (/admin): sites, stats, filters, per-booking timeline, per-conversion retry, reconciliation, CSV export
```

A **site** (Admin → Sites) holds everything about one client: what it reports (leads, sales or both),
the lead and sale postback URLs, the Zylux webhook secret, and its website address. Nothing about what
gets reported is in the script on the client's site, so it can't be changed from the browser.

| Booking | Lead | Sale |
|---|---|---|
| Free meeting booked | sent | not eligible |
| Paid meeting booked, awaiting payment | sent (needs `BOOKING_PAYMENT_INITIATED`, else sent with the sale) | waiting |
| Paid meeting paid | sent | sent, with the amount |
| Needs host approval | waiting until accepted | waiting |
| Cancelled / rejected before sending | not eligible | not eligible |

| Folder | What |
|---|---|
| `api/` | .NET 10 Web API + EF Core + PostgreSQL. Tracker endpoint, Zylux webhook, TrackCrest postback worker, admin API |
| `web/` | Next.js 16. `public/tracker.js`, `/booking-confirmed`, `/admin`, `/demo` (dev only) |
| `scripts/simulate-booking.sh` | Fakes a full paid booking (tracker call + signed webhooks) for testing |

## Run locally

Requires .NET 10 SDK, Node 20+, PostgreSQL on localhost:5432.

```bash
# 1. API  (creates the database and tables on first start)  → http://localhost:5145
cd api
dotnet run --launch-profile http

# 2. Web (second terminal)  → http://localhost:3000/admin
cd web
npm install          # if npm complains about ~/.npm permissions: npm install --cache /tmp/npm-cache
npm run dev
```

Admin login: `ADMIN_USERNAME` / `ADMIN_PASSWORD` in `web/.env.local`.

API docs: Swagger UI at http://localhost:5145/swagger (OpenAPI JSON at `/openapi/v1.json`). Development only, or set
`EnableSwagger=true`. For admin endpoints click **Authorize** and paste `Admin:ApiKey`.

Test without real bookings:
- `scripts/simulate-booking.sh <site-key> <webhook-secret>` — sends a free booking, a paid one (webhooks only), a paid one
  through tracker.js and an untracked one for that site.
- `http://localhost:3000/demo?click_id=test12345&sub1=fb` — runs tracker.js and fires a fake `scheduler:booked`
  (recorded as *Unverified* since no webhook follows).

To test postbacks without TrackCrest, set a site's postback URLs to the API's own health endpoint, e.g.
`http://localhost:5145/health?event={event}&click_id={click_id}`.

## Configuration

API (`api/appsettings.json`, override with environment variables):

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `Admin__ApiKey` | Shared secret; must equal `ADMIN_API_KEY` in the web app |
| `Tracking__RequireWebhookConfirmation` | `true` (recommended): only Zylux-verified bookings are sent to TrackCrest |
| `TrackCrest__MaxAttempts`, `TrackCrest__TimeoutSeconds` | Postback retries and timeout |
| `TrackCrest__PostbackUrl`, `Zylux__WebhookSecret`, `Tracking__AllowedOrigins__0` | Old single-site settings. Only read on the first start with no sites, to create a site called `default` (sale postback = the old URL; set its lead URL in the admin). |

Per site (Admin → Sites): key, name, mode (Lead / Sale / Both), lead and sale postback URLs, website
addresses and the Zylux webhook secret. Postback placeholders (URL-encoded):

| Placeholder | Value |
|---|---|
| `{click_id}` | TrackCrest click ID |
| `{conversion_id}` | `<booking uid>-lead` / `<booking uid>-sale`: use as the transaction ID with Both, so the sale isn't a duplicate of the lead |
| `{event}` | `lead` or `sale` |
| `{txid}` / `{booking_uid}` | Zylux booking uid |
| `{amount}` `{currency}` | Booking price |
| `{status}` | Booking status |
| `{affiliate_id}` `{sub1}`…`{sub5}` | From the landing URL (`aff_id`, `sub1`…) |

Web: see `web/.env.example`.

## Go live

1. Deploy the API and web app on public **HTTPS** URLs (Zylux webhooks require HTTPS). Run one API instance
   (the postback worker isn't coordinated across instances).
2. Set production secrets (new `Admin__ApiKey`, `SESSION_SECRET`, `ADMIN_PASSWORD`, DB password) as environment variables.
3. Admin → Sites → **Add site**. The site page then shows everything below, filled in for that site.
4. TrackCrest offer destination URL → `https://dept.habyyer.com/?click_id={click_id}` (TrackCrest's macro name).
5. Zylux Meet (the site owner's account) → Settings → Developer → Webhooks → `https://YOUR-API/api/webhooks/zylux/{site-key}`
   with the site's secret and the booking triggers, including `BOOKING_PAYMENT_INITIATED`.
6. On dept.habyyer.com, add to `app/layout.tsx`:
   ```tsx
   <Script src="https://YOUR-WEB/tracker.js" strategy="afterInteractive" data-booking-tracker data-site="dept-habyyer"
           data-api="https://YOUR-API" data-confirm-url="https://YOUR-WEB/booking-confirmed" />
   ```
   and add `data-tracking="on"` to the site's Zylux Meet embed script, so Zylux keeps the click ID on the booking
   even when the visitor books on another page or later (off by default, as it stores the click ID in the browser):
   ```html
   <script src="https://meet.zyluxmedia.app/embed.js" data-tracking="on" async></script>
   ```
7. Check **Admin → Setup**: every item should show a tick after the first real booking.

Webhooks set up before sites existed (`/api/webhooks/zylux`, no key) still work: the site is the one whose
secret matches the signature.

## Cleaning up test data

```sql
DELETE FROM tracked_bookings WHERE "BookingUid" LIKE 'bk_sim%' OR "BookingUid" LIKE 'bk_demo%' OR "BookingUid" = 'bk_x1';
```
