# Booking Tracker — Zylux Meet × TrackCrest

Records Zylux Meet bookings that come through a TrackCrest tracking link, reports them to TrackCrest
server-to-server, and gives an admin for reconciliation with the business owner.

```
Affiliate link → TrackCrest → https://dept.habyyer.com/?click_id=…
   tracker.js saves the click ID on the client's site
   visitor books (inline scheduler on /contact or the "Book Consultation" popup)
   "scheduler:booked" → tracker.js → POST /api/track/booking  {uid, clickId, sub1…}
                      → redirect to  /booking-confirmed
Zylux webhooks (BOOKING_CREATED / BOOKING_PAID / …, HMAC-signed) → POST /api/webhooks/zylux
   booking verified + paid → API calls the TrackCrest postback (retries with backoff)
Admin (/admin): stats, filters, per-booking timeline, retry, reconciliation notes, CSV export
```

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
- `scripts/simulate-booking.sh` — creates a verified, paid booking and sends the postback.
- `http://localhost:3000/demo?click_id=test12345&sub1=fb` — runs tracker.js and fires a fake `scheduler:booked`
  (recorded as *Unverified* since no webhook follows).

To test the postback without TrackCrest, point it at the API's own health endpoint:
`TrackCrest__PostbackUrl='http://localhost:5145/health?click_id={click_id}' dotnet run --launch-profile http`

## Configuration

API (`api/appsettings.json`, override with environment variables, e.g. `TrackCrest__PostbackUrl`):

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `TrackCrest__PostbackUrl` | Postback URL from TrackCrest with placeholders `{click_id} {txid} {amount} {currency} {status} {affiliate_id} {sub1}…{sub5}` |
| `Zylux__WebhookSecret` | Same secret as the Zylux webhook |
| `Tracking__AllowedOrigins__0` | `https://dept.habyyer.com` (sites allowed to call the tracker endpoint) |
| `Tracking__RequireWebhookConfirmation` | `true` (recommended): only Zylux-verified bookings are sent to TrackCrest |
| `Admin__ApiKey` | Shared secret; must equal `ADMIN_API_KEY` in the web app |

Web: see `web/.env.example`.

## Go live

1. Deploy the API and web app on public **HTTPS** URLs (Zylux webhooks require HTTPS). Run one API instance
   (the postback worker isn't coordinated across instances).
2. Set production secrets (new `Admin__ApiKey`, `SESSION_SECRET`, `ADMIN_PASSWORD`, DB password) as environment variables.
3. Zylux Meet → Settings → Developer → Webhooks → `https://YOUR-API/api/webhooks/zylux`, booking triggers, same secret.
4. TrackCrest offer destination URL → `https://dept.habyyer.com/?click_id={click_id}` (TrackCrest's macro name).
5. On dept.habyyer.com, add to `app/layout.tsx`:
   ```tsx
   <Script src="https://YOUR-WEB/tracker.js" strategy="afterInteractive" data-booking-tracker
           data-api="https://YOUR-API" data-confirm-url="https://YOUR-WEB/booking-confirmed" />
   ```
6. Check **Admin → Setup**: every item should show a tick after the first real booking.

## Cleaning up test data

```sql
DELETE FROM tracked_bookings WHERE "BookingUid" LIKE 'bk_sim%' OR "BookingUid" LIKE 'bk_demo%' OR "BookingUid" = 'bk_x1';
```
