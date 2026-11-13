using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BookingTracker.Api.Data;
using BookingTracker.Api.Models;
using BookingTracker.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingTracker.Api.Endpoints;

public record BookingQuery(
    string? Search, string? Status, string? Conversion, string? Reconciliation, string? Verified,
    string? From, string? To, int? Page, int? PageSize, Guid? Site = null, string? AffiliatePayment = null);

public record ReconciliationUpdate(string? Status, string? Notes);

/// <param name="Status">Pending | Paid | Rejected</param>
public record AffiliatePaymentUpdate(string? Status);

public record SiteLogin(string? Email, string? Password);

/// <param name="Password">Required for a new user; leave out to keep the current one.</param>
public record SiteUserInput(string? Email, string? Name, string? Password, bool? Active);

public record SiteUserView(Guid Id, Guid SiteId, string Email, string Name, bool Active, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt)
{
    public static SiteUserView From(SiteUser u) => new(u.Id, u.SiteId, u.Email, u.Name, u.Active, u.LastLoginAt, u.CreatedAt);
}

/// <summary>Marks an admin endpoint a site user may call (with X-Site-Scope). Every other admin endpoint refuses scoped calls.</summary>
public sealed class SiteScopeAllowed;

/// <param name="Mode">Lead | Sale | Both</param>
/// <param name="WebhookSecret">Leave out (or empty) to keep the current one; a new site gets a generated one.</param>
/// <param name="RegenerateSecret">Replace the webhook secret with a new random one.</param>
public record SiteInput(
    string? Key, string? Name, string? Mode, string? LeadPostbackUrl, string? SalePostbackUrl,
    string[]? AllowedOrigins, string? WebhookSecret, bool? RegenerateSecret, bool? Active, string? ConfirmationTheme = null);

/// <summary>A site as the admin sees it. The webhook secret is only returned by GET /sites/{id}.</summary>
public record SiteView(
    Guid Id, string Key, string Name, bool Active, TrackMode Mode, string? LeadPostbackUrl, string? SalePostbackUrl,
    List<string> AllowedOrigins, bool WebhookSecretSet, string? WebhookSecret, string ConfirmationTheme, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static SiteView From(Site s, bool withSecret = false) => new(
        s.Id, s.Key, s.Name, s.Active, s.Mode, s.LeadPostbackUrl, s.SalePostbackUrl, s.AllowedOrigins,
        s.WebhookSecret != "", withSecret ? s.WebhookSecret : null, s.ConfirmationTheme, s.CreatedAt, s.UpdatedAt);
}

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Admin").AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var expected = http.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value.ApiKey;
            var given = http.Request.Headers["X-Admin-Key"].ToString();
            if (string.IsNullOrEmpty(expected) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
                return Results.Unauthorized();

            // The web app sends X-Site-Scope for a signed-in site user: only that site's bookings, read-only.
            var scope = http.Request.Headers["X-Site-Scope"].ToString();
            if (scope.Length > 0)
            {
                if (!Guid.TryParse(scope, out var siteId)) return Results.Json(new { error = "Bad site scope." }, statusCode: StatusCodes.Status403Forbidden);
                if (http.GetEndpoint()?.Metadata.GetMetadata<SiteScopeAllowed>() is null)
                    return Results.Json(new { error = "Only the super admin can do this." }, statusCode: StatusCodes.Status403Forbidden);
                http.Items[ScopeKey] = siteId;
            }
            return await next(ctx);
        });

        admin.MapGet("/bookings", async ([AsParameters] BookingQuery q, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var page = Math.Max(1, q.Page ?? 1);
            var size = Math.Clamp(q.PageSize ?? 25, 1, 200);
            var query = Filter(db.Bookings.AsNoTracking(), Scoped(q, http));
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * size).Take(size)
                .ToListAsync(ct);
            return Results.Ok(new { items, total, page, pageSize = size });
        }).WithMetadata(new SiteScopeAllowed()).WithSummary("List tracked bookings").WithDescription("Filters: search (uid, click ID, name, email, source), site (id), status, conversion, reconciliation, affiliatePayment, verified=yes|no, from/to (yyyy-MM-dd, tracked date, UTC). Paged: page, pageSize (max 200).");

        admin.MapGet("/bookings/{id:guid}", async (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var scope = ScopeOf(http);
            var b = await db.Bookings.AsNoTracking()
                .Include(x => x.Events.OrderByDescending(e => e.ReceivedAt))
                .Include(x => x.Conversions.OrderBy(c => c.Kind))
                .Include(x => x.Attempts.OrderByDescending(a => a.AttemptedAt))
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id && (scope == null || x.SiteId == scope), ct);
            if (b is null) return Results.NotFound();
            if (scope is not null) b.Attempts = []; // postback URLs carry TrackCrest tokens
            return Results.Ok(b);
        }).WithMetadata(new SiteScopeAllowed()).WithSummary("Get one booking with its webhook events, conversions and TrackCrest attempts");

        admin.MapPatch("/bookings/{id:guid}/affiliate-payment", async (Guid id, AffiliatePaymentUpdate body, AppDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<AffiliatePaymentStatus>(body.Status, true, out var s) || !Enum.IsDefined(s))
                return Results.BadRequest(new { error = "Status must be Pending, Paid or Rejected." });
            var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return Results.NotFound();
            if (b.AffiliatePaymentStatus != s)
            {
                b.AffiliatePaymentStatus = s;
                b.AffiliatePaymentDate = s == AffiliatePaymentStatus.Pending ? null : DateTimeOffset.UtcNow;
                b.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { b.Id, b.AffiliatePaymentStatus, b.AffiliatePaymentDate });
        }).WithSummary("Set the affiliate payment (Pending | Paid | Rejected)").WithDescription("Paid or Rejected stamps the affiliate payment date with now; Pending clears it. Super admin only.");

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

        // Send one of the booking's conversions (lead or sale) now, unless it was already sent.
        admin.MapPost("/bookings/{id:guid}/conversions/{kind}/send", async (Guid id, string kind, AppDbContext db, ConversionSender sender, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ConversionKind>(kind, true, out var k)) return Results.BadRequest(new { error = "kind must be lead or sale" });
            var b = await db.Bookings.Include(x => x.Conversions).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return Results.NotFound();
            var c = b.Conversions.FirstOrDefault(x => x.Kind == k);
            if (c?.Status == ConversionStatus.Sent) return Results.Conflict(new { error = $"The {kind.ToLowerInvariant()} was already sent; sending again would double-count." });
            if (c is null)
            {
                // The site doesn't report this kind (or the booking predates it): send it anyway, as asked.
                var now = DateTimeOffset.UtcNow;
                c = new Conversion { Id = Guid.NewGuid(), TrackedBookingId = b.Id, Kind = k, CreatedAt = now, UpdatedAt = now };
                db.Conversions.Add(c);
                b.Conversions.Add(c);
            }
            c.AttemptCount = 0;
            var error = await sender.SendAsync(b, c, manual: true, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { status = c.Status, error });
        }).WithSummary("Send (or retry) the lead or sale postback now").WithDescription("kind: lead | sale. Refused with 409 if that conversion was already sent.");

        admin.MapGet("/stats", async ([AsParameters] BookingQuery q, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var query = Filter(db.Bookings.AsNoTracking(), Scoped(q, http) with { Page = null, PageSize = null });
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
                    conversionsSent = g.Count(x => x.Conversions.Any(c => c.Status == ConversionStatus.Sent)),
                })
                .OrderByDescending(x => x.bookings).Take(10).ToListAsync(ct);
            var cancelledAfterSent = await query.CountAsync(b => b.Conversions.Any(c => c.Status == ConversionStatus.Sent) &&
                (b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Rejected), ct);
            var byKind = await db.Conversions.Where(c => query.Select(b => b.Id).Contains(c.TrackedBookingId))
                .GroupBy(c => new { c.Kind, c.Status }).Select(g => new { g.Key.Kind, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
            var unverified = await query.CountAsync(b => !b.WebhookConfirmed, ct);
            var byAffiliate = await query.GroupBy(b => b.AffiliatePaymentStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

            return Results.Ok(new
            {
                total = byStatus.Sum(x => x.Count),
                byStatus = byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
                byConversion = byConversion.ToDictionary(x => x.Key.ToString(), x => x.Count),
                byReconciliation = byRecon.ToDictionary(x => x.Key.ToString(), x => x.Count),
                byAffiliatePayment = byAffiliate.ToDictionary(x => x.Key.ToString(), x => x.Count),
                leads = byKind.Where(x => x.Kind == ConversionKind.Lead).ToDictionary(x => x.Status.ToString(), x => x.Count),
                sales = byKind.Where(x => x.Kind == ConversionKind.Sale).ToDictionary(x => x.Status.ToString(), x => x.Count),
                revenue,
                sources,
                cancelledAfterSent,
                unverified,
            });
        }).WithMetadata(new SiteScopeAllowed()).WithSummary("Totals by status, conversion and review, revenue and top sources").WithDescription("Accepts the same filters as the bookings list.");

        // What's configured on the API (no secrets), for the admin Setup page.
        admin.MapGet("/config", async (IOptions<TrackingOptions> tr, AppDbContext db, CancellationToken ct) =>
        {
            var lastWebhook = await db.WebhookEvents.OrderByDescending(e => e.ReceivedAt).Select(e => (DateTimeOffset?)e.ReceivedAt).FirstOrDefaultAsync(ct);
            var lastTracked = await db.Bookings.OrderByDescending(b => b.CreatedAt).Select(b => (DateTimeOffset?)b.CreatedAt).FirstOrDefaultAsync(ct);
            var lastSent = await db.Conversions.Where(c => c.SentAt != null).OrderByDescending(c => c.SentAt).Select(c => c.SentAt).FirstOrDefaultAsync(ct);
            var sites = await db.Sites.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(ct);
            var webhookBySite = await db.WebhookEvents.Where(e => e.SiteId != null).GroupBy(e => e.SiteId)
                .Select(g => new { g.Key, At = g.Max(e => e.ReceivedAt) }).ToListAsync(ct);
            var trackedBySite = await db.Bookings.Where(b => b.SiteId != null).GroupBy(b => b.SiteId)
                .Select(g => new { g.Key, At = g.Max(b => b.CreatedAt) }).ToListAsync(ct);
            return Results.Ok(new
            {
                requireWebhookConfirmation = tr.Value.RequireWebhookConfirmation,
                lastWebhookAt = lastWebhook,
                lastTrackedAt = lastTracked,
                lastConversionSentAt = lastSent,
                sites = sites.Select(s => new
                {
                    s.Id, s.Key, s.Name, s.Active, s.Mode, s.AllowedOrigins,
                    leadPostbackConfigured = !string.IsNullOrWhiteSpace(s.LeadPostbackUrl),
                    salePostbackConfigured = !string.IsNullOrWhiteSpace(s.SalePostbackUrl),
                    webhookSecretConfigured = s.WebhookSecret != "",
                    lastWebhookAt = webhookBySite.FirstOrDefault(x => x.Key == s.Id)?.At,
                    lastTrackedAt = trackedBySite.FirstOrDefault(x => x.Key == s.Id)?.At,
                }),
            });
        }).WithSummary("What is configured on the API (no secrets)");

        // ---- Sites ----
        admin.MapGet("/sites", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var scope = ScopeOf(http);
            var sites = await db.Sites.AsNoTracking().Where(s => scope == null || s.Id == scope).OrderBy(s => s.CreatedAt).ToListAsync(ct);
            return Results.Ok(sites.Select(s => SiteView.From(s)));
        }).WithMetadata(new SiteScopeAllowed()).WithSummary("List sites (without webhook secrets)").WithDescription("A site user only gets their own site.");

        admin.MapGet("/sites/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) is { } s ? Results.Ok(SiteView.From(s, withSecret: true)) : Results.NotFound())
            .WithSummary("Get one site, including its webhook secret");

        admin.MapPost("/sites", async (SiteInput body, AppDbContext db, SiteOriginCache origins, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var site = new Site { Id = Guid.NewGuid(), CreatedAt = now };
            if (Apply(site, body, isNew: true) is { } error) return Results.BadRequest(new { error });
            if (await db.Sites.AnyAsync(s => s.Key == site.Key, ct)) return Results.Conflict(new { error = $"A site with the key \"{site.Key}\" already exists." });
            site.UpdatedAt = now;
            db.Sites.Add(site);
            await db.SaveChangesAsync(ct);
            await origins.RefreshAsync(db, ct);
            return Results.Ok(SiteView.From(site, withSecret: true));
        }).WithSummary("Create a site").WithDescription("A webhook secret is generated when none is given.");

        admin.MapPut("/sites/{id:guid}", async (Guid id, SiteInput body, AppDbContext db, SiteOriginCache origins, CancellationToken ct) =>
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (site is null) return Results.NotFound();
            if (Apply(site, body, isNew: false) is { } error) return Results.BadRequest(new { error });
            if (await db.Sites.AnyAsync(s => s.Key == site.Key && s.Id != id, ct)) return Results.Conflict(new { error = $"A site with the key \"{site.Key}\" already exists." });
            site.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await origins.RefreshAsync(db, ct);
            return Results.Ok(SiteView.From(site, withSecret: true));
        }).WithSummary("Update a site").WithDescription("Fields left out keep their value. Changing the mode applies to bookings from then on.");

        admin.MapGet("/bookings/export", async ([AsParameters] BookingQuery q, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var rows = await Filter(db.Bookings.AsNoTracking().Include(b => b.Conversions), Scoped(q, http)).OrderByDescending(b => b.CreatedAt).Take(50_000).ToListAsync(ct);
            var siteKeys = await db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Key, ct);
            string Kind(TrackedBooking b, ConversionKind k) => b.Conversions.FirstOrDefault(c => c.Kind == k)?.Status.ToString() ?? "";
            var sb = new StringBuilder();
            string[] header =
            [
                "Tracked at (UTC)", "Booking UID", "Click ID", "Affiliate ID", "Sub1", "Sub2", "Sub3", "Title", "Start (UTC)",
                "Attendee name", "Attendee email", "Booking status", "Webhook confirmed", "Payment required", "Amount", "Currency",
                "Paid at (UTC)", "Conversion status", "Lead", "Sale", "Conversion sent at (UTC)", "Reconciliation", "Notes", "Affiliate payment", "Affiliate payment date (UTC)", "Landing URL", "Site",
            ];
            sb.AppendLine(string.Join(',', header.Select(Csv)));
            foreach (var b in rows)
            {
                string?[] cols =
                [
                    D(b.CreatedAt), b.BookingUid, b.ClickId, b.AffiliateId, b.Sub1, b.Sub2, b.Sub3, b.Title, D(b.StartTime),
                    b.AttendeeName, b.AttendeeEmail, b.Status.ToString(), b.WebhookConfirmed ? "yes" : "no", b.PaymentRequired ? "yes" : "no",
                    b.Amount?.ToString(CultureInfo.InvariantCulture), b.Currency, D(b.PaidAt), b.ConversionStatus.ToString(),
                    Kind(b, ConversionKind.Lead), Kind(b, ConversionKind.Sale),
                    D(b.ConversionSentAt), b.ReconciliationStatus.ToString(), b.ReconciliationNotes,
                    b.AffiliatePaymentStatus.ToString(), D(b.AffiliatePaymentDate), b.LandingUrl,
                    b.SiteId is { } sid && siteKeys.TryGetValue(sid, out var key) ? key : "",
                ];
                sb.AppendLine(string.Join(',', cols.Select(Csv)));
            }
            var name = $"tracked-bookings-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", name);
        }).WithMetadata(new SiteScopeAllowed()).WithSummary("Export bookings as CSV").WithDescription("Accepts the same filters as the bookings list.");

        // ---- Site users (logins that see one site's bookings) ----
        admin.MapGet("/sites/{id:guid}/users", async (Guid id, AppDbContext db, CancellationToken ct) =>
            Results.Ok((await db.SiteUsers.AsNoTracking().Where(u => u.SiteId == id).OrderBy(u => u.CreatedAt).ToListAsync(ct)).Select(SiteUserView.From)))
            .WithSummary("List a site's users");

        admin.MapPost("/sites/{id:guid}/users", async (Guid id, SiteUserInput body, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Sites.AnyAsync(s => s.Id == id, ct)) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            var user = new SiteUser { Id = Guid.NewGuid(), SiteId = id, CreatedAt = now, UpdatedAt = now };
            if (ApplyUser(user, body, isNew: true) is { } error) return Results.BadRequest(new { error });
            if (await db.SiteUsers.AnyAsync(u => u.Email == user.Email, ct)) return Results.Conflict(new { error = $"{user.Email} already has a login." });
            db.SiteUsers.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Ok(SiteUserView.From(user));
        }).WithSummary("Add a user to a site").WithDescription("Email, name and password (8+ characters) are required.");

        admin.MapPut("/site-users/{id:guid}", async (Guid id, SiteUserInput body, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.SiteUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            if (ApplyUser(user, body, isNew: false) is { } error) return Results.BadRequest(new { error });
            if (await db.SiteUsers.AnyAsync(u => u.Email == user.Email && u.Id != id, ct)) return Results.Conflict(new { error = $"{user.Email} already has a login." });
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(SiteUserView.From(user));
        }).WithSummary("Update a site user (name, email, active, new password)").WithDescription("Fields left out keep their value.");

        admin.MapDelete("/site-users/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var n = await db.SiteUsers.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        }).WithSummary("Delete a site user");

        // Called by the web app's login form (server-side, with the admin key).
        admin.MapPost("/auth/site-login", async (SiteLogin body, AppDbContext db, CancellationToken ct) =>
        {
            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            var user = email.Length == 0 ? null : await db.SiteUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is null || !user.Active || string.IsNullOrEmpty(body.Password)) return Results.Unauthorized();
            var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, body.Password);
            if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();
            if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = Hasher.HashPassword(user, body.Password);
            var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == user.SiteId, ct);
            if (site is null) return Results.Unauthorized();
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user = SiteUserView.From(user), site = new { site.Id, site.Key, site.Name } });
        }).WithSummary("Check a site user's email and password").WithDescription("401 when wrong or the user is turned off.");

        // The web app checks this on every request, so turning a user off (or deleting them) signs them out.
        admin.MapGet("/site-users/{id:guid}/session", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.SiteUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.Active, ct);
            if (user is null) return Results.NotFound();
            var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == user.SiteId, ct);
            return site is null ? Results.NotFound() : Results.Ok(new { user = SiteUserView.From(user), site = new { site.Id, site.Key, site.Name } });
        }).WithSummary("Whether a site user may still be signed in");
    }

    const string ScopeKey = "SiteScope";
    static readonly PasswordHasher<SiteUser> Hasher = new();
    static readonly System.Text.RegularExpressions.Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    /// <summary>The site a site user is limited to, or null for the super admin.</summary>
    static Guid? ScopeOf(HttpContext http) => http.Items.TryGetValue(ScopeKey, out var v) && v is Guid g ? g : null;

    /// <summary>Forces the site filter for a site user, whatever site they asked for.</summary>
    static BookingQuery Scoped(BookingQuery q, HttpContext http) => ScopeOf(http) is { } site ? q with { Site = site } : q;

    static string? ApplyUser(SiteUser user, SiteUserInput body, bool isNew)
    {
        if (body.Email is not null || isNew)
        {
            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length > 200 || !EmailRx.IsMatch(email)) return "Enter a valid email address.";
            user.Email = email;
        }
        if (body.Name is not null || isNew)
        {
            var name = (body.Name ?? "").Trim();
            if (name.Length is 0 or > 200) return "Name is required (up to 200 characters).";
            user.Name = name;
        }
        if (!string.IsNullOrEmpty(body.Password) || isNew)
        {
            var pw = body.Password ?? "";
            if (pw.Length is < 8 or > 200) return "Password must be 8–200 characters.";
            user.PasswordHash = Hasher.HashPassword(user, pw);
        }
        if (body.Active is { } active) user.Active = active;
        return null;
    }

    static readonly System.Text.RegularExpressions.Regex KeyRx = new("^[a-z0-9][a-z0-9-]{1,49}$");

    /// <summary>Validates <paramref name="body"/> onto <paramref name="site"/>. Returns an error message, or null.</summary>
    static string? Apply(Site site, SiteInput body, bool isNew)
    {
        if (body.Key is not null || isNew)
        {
            var key = (body.Key ?? "").Trim().ToLowerInvariant();
            if (!KeyRx.IsMatch(key)) return "Key: 2–50 lowercase letters, digits and dashes, e.g. dept-habyyer.";
            site.Key = key;
        }
        if (body.Name is not null || isNew)
        {
            var name = (body.Name ?? "").Trim();
            if (name.Length is 0 or > 200) return "Name is required (up to 200 characters).";
            site.Name = name;
        }
        if (body.Mode is not null)
        {
            if (!Enum.TryParse<TrackMode>(body.Mode, true, out var mode)) return "Mode must be Lead, Sale or Both.";
            site.Mode = mode;
        }
        foreach (var (value, set, label) in new (string?, Action<string?>, string)[]
                 {
                     (body.LeadPostbackUrl, v => site.LeadPostbackUrl = v, "Lead postback URL"),
                     (body.SalePostbackUrl, v => site.SalePostbackUrl = v, "Sale postback URL"),
                 })
        {
            if (value is null) continue;
            var url = value.Trim();
            if (url.Length == 0) { set(null); continue; }
            if (url.Length > 2000 || !Uri.TryCreate(url.Replace("{", "").Replace("}", ""), UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http"))
                return $"{label} must be a full http(s) URL.";
            set(url);
        }
        if (body.AllowedOrigins is not null)
        {
            var list = new List<string>();
            foreach (var o in body.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)))
            {
                var n = SiteOriginCache.NormalizeOrigin(o);
                if (n is null) return $"\"{o.Trim()}\" isn't a website address like https://example.com.";
                if (!list.Contains(n)) list.Add(n);
            }
            site.AllowedOrigins = list;
        }
        if (body.RegenerateSecret == true || (isNew && string.IsNullOrWhiteSpace(body.WebhookSecret)))
            site.WebhookSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        else if (!string.IsNullOrWhiteSpace(body.WebhookSecret))
        {
            var secret = body.WebhookSecret.Trim();
            if (secret.Length is < 16 or > 200) return "Webhook secret must be 16–200 characters.";
            site.WebhookSecret = secret;
        }
        if (body.Active is { } active) site.Active = active;
        if (body.ConfirmationTheme is not null)
        {
            var theme = body.ConfirmationTheme.Trim().ToLowerInvariant();
            if (theme.Length == 0) theme = ConfirmationThemes.Default;
            if (!ConfirmationThemes.All.Contains(theme)) return $"Theme must be one of: {string.Join(", ", ConfirmationThemes.All)}.";
            site.ConfirmationTheme = theme;
        }
        return null;
    }

    static IQueryable<TrackedBooking> Filter(IQueryable<TrackedBooking> q, BookingQuery f)
    {
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = "%" + f.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(b =>
                EF.Functions.ILike(b.BookingUid, s, "\\") || EF.Functions.ILike(b.ClickId, s, "\\") ||
                (b.AttendeeEmail != null && EF.Functions.ILike(b.AttendeeEmail, s, "\\")) ||
                (b.AttendeeName != null && EF.Functions.ILike(b.AttendeeName, s, "\\")) ||
                (b.AffiliateId != null && EF.Functions.ILike(b.AffiliateId, s, "\\")) ||
                (b.Sub1 != null && EF.Functions.ILike(b.Sub1, s, "\\")) ||
                (b.Title != null && EF.Functions.ILike(b.Title, s, "\\")));
        }
        if (f.Site is { } site) q = q.Where(b => b.SiteId == site);
        if (Enum.TryParse<BookingStatus>(f.Status, true, out var st)) q = q.Where(b => b.Status == st);
        if (Enum.TryParse<ConversionStatus>(f.Conversion, true, out var cs)) q = q.Where(b => b.ConversionStatus == cs);
        if (Enum.TryParse<ReconciliationStatus>(f.Reconciliation, true, out var rs)) q = q.Where(b => b.ReconciliationStatus == rs);
        if (Enum.TryParse<AffiliatePaymentStatus>(f.AffiliatePayment, true, out var ap)) q = q.Where(b => b.AffiliatePaymentStatus == ap);
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
