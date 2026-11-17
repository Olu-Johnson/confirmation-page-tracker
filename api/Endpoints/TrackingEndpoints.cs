using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookingTracker.Api.Data;
using BookingTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using BookingTracker.Api.Services;

namespace BookingTracker.Api.Endpoints;

/// <summary>Body sent by tracker.js after Zylux fires "scheduler:booked".</summary>
/// <param name="Uid">Zylux booking uid, e.g. bk_7h2k9…</param>
/// <param name="ClickId">TrackCrest click ID captured from the landing URL.</param>
/// <param name="Status">accepted | pending | awaiting_payment</param>
/// <param name="ClickedAt">When the click was captured (Unix milliseconds).</param>
/// <param name="SiteKey">The site's key from Admin → Sites (data-site on the script tag). Optional: without it the site is found by the Origin.</param>
/// <param name="Site">Origin of the page the tracker ran on.</param>
public record TrackBody(
    string? Uid, string? ClickId, string? AffiliateId, string? Status, string? Title,
    string? StartTime, string? EndTime, string? Sub1, string? Sub2, string? Sub3, string? Sub4, string? Sub5,
    string? Site, string? LandingUrl, string? Referrer, long? ClickedAt, string? SiteKey = null);

public static partial class TrackingEndpoints
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{3,100}$")] private static partial Regex UidRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{4,128}$")] private static partial Regex ClickRx();

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapTrackingEndpoints(this WebApplication app)
    {
        // Called by tracker.js from the client's site. Sent as text/plain so browsers skip the CORS preflight.
        // Public: what the booking confirmation page should look like. By site key (the page's
        // /booking-confirmed/{key} URL), else by the booking's uid. Only the name, website and theme.
        app.MapGet("/api/public/confirmation", async (string? site, string? uid, AppDbContext db, CancellationToken ct) =>
        {
            Site? s = null;
            if (!string.IsNullOrWhiteSpace(site))
                s = await db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.Key == site.Trim().ToLower() && x.Active, ct);
            if (s is null && !string.IsNullOrWhiteSpace(uid) && uid.Length <= 100)
            {
                var siteId = await db.Bookings.AsNoTracking().Where(b => b.BookingUid == uid || b.OriginalBookingUid == uid)
                    .Select(b => b.SiteId).FirstOrDefaultAsync(ct);
                if (siteId is { } id) s = await db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Active, ct);
            }
            if (s is null) return Results.NotFound();
            return Results.Ok(new { key = s.Key, name = s.Name, websiteUrl = s.AllowedOrigins.FirstOrDefault(), theme = s.ConfirmationTheme });
        }).WithTags("Tracking").WithSummary("Confirmation page settings for a site (by key or booking uid)");

        app.MapPost("/api/track/booking", async (HttpRequest req, BookingEventService svc, SiteService sites, CancellationToken ct) =>
        {
            var raw = await ReadLimitedAsync(req.Body, 16 * 1024, ct);
            if (raw is null) return Results.StatusCode(413);

            TrackBody? body;
            try { body = JsonSerializer.Deserialize<TrackBody>(raw, Json); }
            catch (JsonException) { return Results.BadRequest(new { error = "invalid json" }); }

            // The site: by the key on the script tag, else by the page's origin. CORS alone doesn't stop a
            // "simple" request from being processed, so the Origin is checked against the site here too.
            var origin = req.Headers.Origin.ToString();
            var key = body?.SiteKey?.Trim();
            var site = !string.IsNullOrEmpty(key) ? await sites.ByKeyAsync(key, ct) : await sites.ByOriginAsync(origin, ct);
            if (site is null) return Results.StatusCode(403);
            if (!string.IsNullOrEmpty(origin) && !site.AllowedOrigins.Any(o =>
                    string.Equals(SiteOriginCache.NormalizeOrigin(o), SiteOriginCache.NormalizeOrigin(origin), StringComparison.OrdinalIgnoreCase)))
                return Results.StatusCode(403);

            var uid = body?.Uid?.Trim() ?? "";
            var clickId = body?.ClickId?.Trim() ?? "";
            if (!UidRx().IsMatch(uid) || !ClickRx().IsMatch(clickId))
                return Results.BadRequest(new { error = "uid and clickId are required" });

            var input = new TrackerInput(
                uid, clickId, Clean(body!.AffiliateId, 100), BookingEventService.ParseStatus(body.Status), Clean(body.Title, 300),
                ParseDate(body.StartTime), ParseDate(body.EndTime),
                [Clean(body.Sub1, 100), Clean(body.Sub2, 100), Clean(body.Sub3, 100), Clean(body.Sub4, 100), Clean(body.Sub5, 100)],
                Clean(body.Site ?? req.Headers.Origin.ToString(), 200), Clean(body.LandingUrl, 1000), Clean(body.Referrer, 1000),
                body.ClickedAt is > 0 and < 4102444800000 ? DateTimeOffset.FromUnixTimeMilliseconds(body.ClickedAt.Value) : null);

            var booking = await svc.RecordTrackerAsync(site, input, ct);
            return Results.Ok(new { ok = true, status = booking.Status.ToString(), conversion = booking.ConversionStatus.ToString() });
        })
        .RequireCors("tracker")
        .RequireRateLimiting("tracker")
        .Accepts<TrackBody>("text/plain", "application/json")
        .WithTags("Tracking")
        .WithSummary("Record a booking from tracker.js")
        .WithDescription("Called by tracker.js on the client's site. Sent as text/plain (JSON body) to avoid a CORS preflight. " +
                         "The site is picked by siteKey (data-site), else by the Origin, which must be one of the site's allowed origins. " +
                         "Limited to 20 requests per minute per IP. " +
                         "The booking stays 'Unverified' until a signed Zylux webhook confirms it.");

        // Zylux Meet webhook (Settings → Developer → Webhooks), one URL per site. Signed with HMAC-SHA256 of the raw body.
        app.MapPost("/api/webhooks/zylux/{siteKey}", async (string siteKey, HttpRequest req, BookingEventService svc, SiteService sites, ILogger<BookingEventService> log, CancellationToken ct) =>
        {
            var site = await sites.ByKeyAsync(siteKey, ct);
            if (site is null) return Results.NotFound(new { error = "unknown site" });
            if (string.IsNullOrEmpty(site.WebhookSecret))
            {
                log.LogError("Site {Site} has no webhook secret; rejecting webhook", site.Key);
                return Results.Problem("Webhook secret not configured for this site", statusCode: 503);
            }
            var raw = await ReadLimitedAsync(req.Body, 512 * 1024, ct);
            if (raw is null) return Results.StatusCode(413);
            if (!SignatureValid(raw, site.WebhookSecret, req.Headers["X-Scheduler-Signature-256"].ToString()))
            {
                log.LogWarning("Rejected webhook for site {Site} with invalid signature", site.Key);
                return Results.Unauthorized();
            }
            return await HandleAsync(site, raw, req, svc, ct);
        })
        .Accepts<JsonElement>("application/json")
        .WithTags("Webhooks")
        .WithSummary("Zylux Meet webhook receiver for one site")
        .WithDescription("Configure in the site's Zylux Meet account → Settings → Developer → Webhooks, with the site's webhook secret " +
                         "(Admin → Sites). Requires header X-Scheduler-Signature-256 (HMAC-SHA256 of the raw body, lowercase hex) and " +
                         "X-Scheduler-Trigger (or triggerEvent in the body). Use scripts/simulate-booking.sh to test.");

        // Older single URL: the site is the one whose secret matches the signature.
        app.MapPost("/api/webhooks/zylux", async (HttpRequest req, BookingEventService svc, SiteService sites, ILogger<BookingEventService> log, CancellationToken ct) =>
        {
            var raw = await ReadLimitedAsync(req.Body, 512 * 1024, ct);
            if (raw is null) return Results.StatusCode(413);
            var signature = req.Headers["X-Scheduler-Signature-256"].ToString();
            var site = (await sites.ActiveAsync(ct)).FirstOrDefault(s => SignatureValid(raw, s.WebhookSecret, signature));
            if (site is null)
            {
                log.LogWarning("Rejected webhook: the signature matches no site's secret");
                return Results.Unauthorized();
            }
            return await HandleAsync(site, raw, req, svc, ct);
        })
        .Accepts<JsonElement>("application/json")
        .WithTags("Webhooks")
        .WithSummary("Zylux Meet webhook receiver (site found by its secret)")
        .WithDescription("Kept for webhooks set up before sites existed. Prefer /api/webhooks/zylux/{siteKey}.");
    }

    static async Task<IResult> HandleAsync(Site site, string raw, HttpRequest req, BookingEventService svc, CancellationToken ct)
    {
        string? trigger = req.Headers["X-Scheduler-Trigger"].ToString();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (string.IsNullOrEmpty(trigger) && doc.RootElement.TryGetProperty("triggerEvent", out var t)) trigger = t.GetString();
        }
        catch (JsonException) { return Results.BadRequest(new { error = "invalid json" }); }
        if (string.IsNullOrWhiteSpace(trigger)) return Results.Ok(new { ok = true, ignored = true });

        var matched = await svc.HandleWebhookAsync(site, trigger.Trim().ToUpperInvariant(), raw, ct);
        return Results.Ok(new { ok = true, site = site.Key, matched });
    }

    public static bool SignatureValid(string body, string secret, string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        var sig = header.Trim();
        if (sig.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) sig = sig[7..];
        byte[] given;
        try { given = Convert.FromHexString(sig); } catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    static async Task<string?> ReadLimitedAsync(Stream body, int limit, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > limit) return null;
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    static DateTimeOffset? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;
}
