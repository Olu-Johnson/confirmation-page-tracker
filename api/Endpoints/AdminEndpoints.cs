using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BookingTracker.Api.Data;
using BookingTracker.Api.Models;
using BookingTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingTracker.Api.Endpoints;

public record BookingQuery(
    string? Search, string? Status, string? Conversion, string? Reconciliation, string? Verified,
    string? From, string? To, int? Page, int? PageSize);

public record ReconciliationUpdate(string? Status, string? Notes);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Admin").AddEndpointFilter(async (ctx, next) =>
        {
            var expected = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value.ApiKey;
            var given = ctx.HttpContext.Request.Headers["X-Admin-Key"].ToString();
            if (string.IsNullOrEmpty(expected) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
                return Results.Unauthorized();
            return await next(ctx);
        });

        admin.MapGet("/bookings", async ([AsParameters] BookingQuery q, AppDbContext db, CancellationToken ct) =>
        {
            var page = Math.Max(1, q.Page ?? 1);
            var size = Math.Clamp(q.PageSize ?? 25, 1, 200);
            var query = Filter(db.Bookings.AsNoTracking(), q);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * size).Take(size)
                .ToListAsync(ct);
            return Results.Ok(new { items, total, page, pageSize = size });
        }).WithSummary("List tracked bookings").WithDescription("Filters: search (uid, click ID, name, email, source), status, conversion, reconciliation, verified=yes|no, from/to (yyyy-MM-dd, tracked date, UTC). Paged: page, pageSize (max 200).");

        admin.MapGet("/bookings/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var b = await db.Bookings.AsNoTracking()
                .Include(x => x.Events.OrderByDescending(e => e.ReceivedAt))
                .Include(x => x.Attempts.OrderByDescending(a => a.AttemptedAt))
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return b is null ? Results.NotFound() : Results.Ok(b);
        }).WithSummary("Get one booking with its webhook events and TrackCrest attempts");

        admin.MapPatch("/bookings/{id:guid}/reconciliation", async (Guid id, ReconciliationUpdate body, AppDbContext db, CancellationToken ct) =>
        {
            var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return Results.NotFound();
            if (body.Status is not null)
            {
                if (!Enum.TryParse<ReconciliationStatus>(body.Status, true, out var s)) return Results.BadRequest(new { error = "invalid status" });
                b.ReconciliationStatus = s;
                b.ReconciledAt = s == ReconciliationStatus.Unreviewed ? null : DateTimeOffset.UtcNow;
            }
            if (body.Notes is not null) b.ReconciliationNotes = body.Notes.Length > 2000 ? body.Notes[..2000] : body.Notes;
            b.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(b);
        }).WithSummary("Set review status (Unreviewed | Confirmed | Disputed) and notes");

        // Re-send the TrackCrest postback now (anything except an already-sent conversion).
        admin.MapPost("/bookings/{id:guid}/send-conversion", async (Guid id, AppDbContext db, ConversionSender sender, CancellationToken ct) =>
        {
            var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return Results.NotFound();
            if (b.ConversionStatus == ConversionStatus.Sent) return Results.Conflict(new { error = "Conversion already sent; sending again would double-count." });
            b.ConversionAttemptCount = 0;
            await sender.SendAsync(b, manual: true, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { b.ConversionStatus, b.LastConversionError });
        }).WithSummary("Send (or retry) the TrackCrest postback now").WithDescription("Refused with 409 if the conversion was already sent.");

        admin.MapGet("/stats", async ([AsParameters] BookingQuery q, AppDbContext db, CancellationToken ct) =>
        {
            var query = Filter(db.Bookings.AsNoTracking(), q with { Page = null, PageSize = null });
            var byStatus = await query.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var byConversion = await query.GroupBy(b => b.ConversionStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var byRecon = await query.GroupBy(b => b.ReconciliationStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var revenue = await query.Where(b => b.Status == BookingStatus.Paid && b.Amount != null)
                .GroupBy(b => b.Currency).Select(g => new { currency = g.Key ?? "", amount = g.Sum(x => x.Amount) ?? 0 }).ToListAsync(ct);
            var sources = await query.GroupBy(b => b.AffiliateId ?? b.Sub1 ?? "(none)")
                .Select(g => new
                {
                    source = g.Key,
                    bookings = g.Count(),
                    paid = g.Count(x => x.Status == BookingStatus.Paid),
                    conversionsSent = g.Count(x => x.ConversionStatus == ConversionStatus.Sent),
                })
                .OrderByDescending(x => x.bookings).Take(10).ToListAsync(ct);
            var cancelledAfterSent = await query.CountAsync(b => b.ConversionStatus == ConversionStatus.Sent &&
                (b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Rejected), ct);
            var unverified = await query.CountAsync(b => !b.WebhookConfirmed, ct);

            return Results.Ok(new
            {
                total = byStatus.Sum(x => x.Count),
                byStatus = byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
                byConversion = byConversion.ToDictionary(x => x.Key.ToString(), x => x.Count),
                byReconciliation = byRecon.ToDictionary(x => x.Key.ToString(), x => x.Count),
                revenue,
                sources,
                cancelledAfterSent,
                unverified,
            });
        }).WithSummary("Totals by status, conversion and review, revenue and top sources").WithDescription("Accepts the same filters as the bookings list.");

        // What's configured on the API (no secrets), for the admin Setup page.
        admin.MapGet("/config", async (IOptions<TrackCrestOptions> tc, IOptions<ZyluxOptions> zy, IOptions<TrackingOptions> tr, AppDbContext db, CancellationToken ct) =>
        {
            var lastWebhook = await db.WebhookEvents.OrderByDescending(e => e.ReceivedAt).Select(e => (DateTimeOffset?)e.ReceivedAt).FirstOrDefaultAsync(ct);
            var lastTracked = await db.Bookings.OrderByDescending(b => b.CreatedAt).Select(b => (DateTimeOffset?)b.CreatedAt).FirstOrDefaultAsync(ct);
            var lastSent = await db.Bookings.Where(b => b.ConversionSentAt != null).OrderByDescending(b => b.ConversionSentAt).Select(b => b.ConversionSentAt).FirstOrDefaultAsync(ct);
            return Results.Ok(new
            {
                postbackConfigured = !string.IsNullOrWhiteSpace(tc.Value.PostbackUrl),
                postbackHost = Uri.TryCreate(tc.Value.PostbackUrl, UriKind.Absolute, out var u) ? u.Host : null,
                webhookSecretConfigured = !string.IsNullOrEmpty(zy.Value.WebhookSecret),
                allowedOrigins = tr.Value.AllowedOrigins,
                requireWebhookConfirmation = tr.Value.RequireWebhookConfirmation,
                lastWebhookAt = lastWebhook,
                lastTrackedAt = lastTracked,
                lastConversionSentAt = lastSent,
            });
        }).WithSummary("What is configured on the API (no secrets)");

        admin.MapGet("/bookings/export", async ([AsParameters] BookingQuery q, AppDbContext db, CancellationToken ct) =>
        {
            var rows = await Filter(db.Bookings.AsNoTracking(), q).OrderByDescending(b => b.CreatedAt).Take(50_000).ToListAsync(ct);
            var sb = new StringBuilder();
            string[] header =
            [
                "Tracked at (UTC)", "Booking UID", "Click ID", "Affiliate ID", "Sub1", "Sub2", "Sub3", "Title", "Start (UTC)",
                "Attendee name", "Attendee email", "Booking status", "Webhook confirmed", "Payment required", "Amount", "Currency",
                "Paid at (UTC)", "Conversion status", "Conversion sent at (UTC)", "Reconciliation", "Notes", "Landing URL",
            ];
            sb.AppendLine(string.Join(',', header.Select(Csv)));
            foreach (var b in rows)
            {
                string?[] cols =
                [
                    D(b.CreatedAt), b.BookingUid, b.ClickId, b.AffiliateId, b.Sub1, b.Sub2, b.Sub3, b.Title, D(b.StartTime),
                    b.AttendeeName, b.AttendeeEmail, b.Status.ToString(), b.WebhookConfirmed ? "yes" : "no", b.PaymentRequired ? "yes" : "no",
                    b.Amount?.ToString(CultureInfo.InvariantCulture), b.Currency, D(b.PaidAt), b.ConversionStatus.ToString(),
                    D(b.ConversionSentAt), b.ReconciliationStatus.ToString(), b.ReconciliationNotes, b.LandingUrl,
                ];
                sb.AppendLine(string.Join(',', cols.Select(Csv)));
            }
            var name = $"tracked-bookings-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", name);
        }).WithSummary("Export bookings as CSV").WithDescription("Accepts the same filters as the bookings list.");
    }

    static IQueryable<TrackedBooking> Filter(IQueryable<TrackedBooking> q, BookingQuery f)
    {
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = "%" + f.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(b =>
                EF.Functions.ILike(b.BookingUid, s) || EF.Functions.ILike(b.ClickId, s) ||
                (b.AttendeeEmail != null && EF.Functions.ILike(b.AttendeeEmail, s)) ||
                (b.AttendeeName != null && EF.Functions.ILike(b.AttendeeName, s)) ||
                (b.AffiliateId != null && EF.Functions.ILike(b.AffiliateId, s)) ||
                (b.Sub1 != null && EF.Functions.ILike(b.Sub1, s)) ||
                (b.Title != null && EF.Functions.ILike(b.Title, s)));
        }
        if (Enum.TryParse<BookingStatus>(f.Status, true, out var st)) q = q.Where(b => b.Status == st);
        if (Enum.TryParse<ConversionStatus>(f.Conversion, true, out var cs)) q = q.Where(b => b.ConversionStatus == cs);
        if (Enum.TryParse<ReconciliationStatus>(f.Reconciliation, true, out var rs)) q = q.Where(b => b.ReconciliationStatus == rs);
        if (f.Verified == "yes") q = q.Where(b => b.WebhookConfirmed);
        else if (f.Verified == "no") q = q.Where(b => !b.WebhookConfirmed);
        if (DateOnly.TryParse(f.From, CultureInfo.InvariantCulture, out var from))
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(b => b.CreatedAt >= start);
        }
        if (DateOnly.TryParse(f.To, CultureInfo.InvariantCulture, out var to))
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(b => b.CreatedAt < end);
        }
        return q;
    }

    static string D(DateTimeOffset? d) => d?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    static string Csv(string? v)
    {
        v ??= "";
        // Neutralise spreadsheet formulas (CSV injection) and quote when needed.
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
