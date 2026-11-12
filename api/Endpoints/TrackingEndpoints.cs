using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookingTracker.Api.Models;
using BookingTracker.Api.Services;
using Microsoft.Extensions.Options;

namespace BookingTracker.Api.Endpoints;

/// <summary>Body sent by tracker.js after Zylux fires "scheduler:booked".</summary>
/// <param name="Uid">Zylux booking uid, e.g. bk_7h2k9…</param>
/// <param name="ClickId">TrackCrest click ID captured from the landing URL.</param>
/// <param name="Status">accepted | pending | awaiting_payment</param>
/// <param name="ClickedAt">When the click was captured (Unix milliseconds).</param>
public record TrackBody(
    string? Uid, string? ClickId, string? AffiliateId, string? Status, string? Title,
    string? StartTime, string? EndTime, string? Sub1, string? Sub2, string? Sub3, string? Sub4, string? Sub5,
    string? Site, string? LandingUrl, string? Referrer, long? ClickedAt);

public static partial class TrackingEndpoints
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{3,100}$")] private static partial Regex UidRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{4,128}$")] private static partial Regex ClickRx();

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapTrackingEndpoints(this WebApplication app)
    {
        // Called by tracker.js from the client's site. Sent as text/plain so browsers skip the CORS preflight.
        app.MapPost("/api/track/booking", async (HttpRequest req, BookingEventService svc, IOptions<TrackingOptions> tracking, CancellationToken ct) =>
        {
            // CORS alone doesn't stop a "simple" request from being processed, so check the Origin here too.
            var origin = req.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) &&
                !tracking.Value.AllowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
                return Results.StatusCode(403);

            var raw = await ReadLimitedAsync(req.Body, 16 * 1024, ct);
            if (raw is null) return Results.StatusCode(413);

            TrackBody? body;
            try { body = JsonSerializer.Deserialize<TrackBody>(raw, Json); }
            catch (JsonException) { return Results.BadRequest(new { error = "invalid json" }); }

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

            var booking = await svc.RecordTrackerAsync(input, ct);
            return Results.Ok(new { ok = true, status = booking.Status.ToString(), conversion = booking.ConversionStatus.ToString() });
        })
        .RequireCors("tracker")
        .RequireRateLimiting("tracker")
        .Accepts<TrackBody>("text/plain", "application/json")
        .WithTags("Tracking")
        .WithSummary("Record a booking from tracker.js")
        .WithDescription("Called by tracker.js on the client's site. Sent as text/plain (JSON body) to avoid a CORS preflight. " +
                         "The Origin must be in Tracking:AllowedOrigins. Limited to 20 requests per minute per IP. " +
                         "The booking stays 'Unverified' until a signed Zylux webhook confirms it.");

        // Zylux Meet webhook (Settings → Developer → Webhooks). Signed with HMAC-SHA256 of the raw body.
        app.MapPost("/api/webhooks/zylux", async (HttpRequest req, BookingEventService svc, IOptions<ZyluxOptions> zylux, ILogger<BookingEventService> log, CancellationToken ct) =>
        {
            var secret = zylux.Value.WebhookSecret;
            if (string.IsNullOrEmpty(secret))
            {
                log.LogError("Zylux:WebhookSecret is not configured; rejecting webhook");
                return Results.Problem("Webhook secret not configured", statusCode: 503);
            }

            var raw = await ReadLimitedAsync(req.Body, 512 * 1024, ct);
            if (raw is null) return Results.StatusCode(413);
            if (!SignatureValid(raw, secret, req.Headers["X-Scheduler-Signature-256"].ToString()))
            {
                log.LogWarning("Rejected webhook with invalid signature");
                return Results.Unauthorized();
            }

            string? trigger = req.Headers["X-Scheduler-Trigger"].ToString();
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (string.IsNullOrEmpty(trigger) && doc.RootElement.TryGetProperty("triggerEvent", out var t)) trigger = t.GetString();
            }
            catch (JsonException) { return Results.BadRequest(new { error = "invalid json" }); }
            if (string.IsNullOrWhiteSpace(trigger)) return Results.Ok(new { ok = true, ignored = true });

            var matched = await svc.HandleWebhookAsync(trigger.Trim().ToUpperInvariant(), raw, ct);
            return Results.Ok(new { ok = true, matched });
        })
        .Accepts<JsonElement>("application/json")
        .WithTags("Webhooks")
        .WithSummary("Zylux Meet webhook receiver")
        .WithDescription("Configure in Zylux Meet → Settings → Developer → Webhooks. Requires header X-Scheduler-Signature-256 " +
                         "(HMAC-SHA256 of the raw body with Zylux:WebhookSecret, lowercase hex) and X-Scheduler-Trigger " +
                         "(or triggerEvent in the body). Can't be called from Swagger without a valid signature; " +
                         "use scripts/simulate-booking.sh to test.");
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
