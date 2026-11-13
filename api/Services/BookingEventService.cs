using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookingTracker.Api.Data;
using BookingTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingTracker.Api.Services;

/// <summary>Validated data posted by tracker.js when the Zylux embed fires "scheduler:booked".</summary>
public record TrackerInput(
    string Uid, string ClickId, string? AffiliateId, BookingStatus? Status, string? Title,
    DateTimeOffset? StartTime, DateTimeOffset? EndTime, string?[] Subs,
    string? Site, string? LandingUrl, string? Referrer, DateTimeOffset? ClickedAt);

/// <summary>Creates tracked bookings and applies Zylux webhook events to them.</summary>
public partial class BookingEventService(
    AppDbContext db, ConversionQueue queue, IOptions<TrackingOptions> tracking, ILogger<BookingEventService> log)
{
    static readonly string[] RescheduleKeys = ["rescheduledFromUid", "fromReschedule", "originalBookingUid", "rescheduleUid", "rescheduledFrom"];

    public async Task<TrackedBooking> RecordTrackerAsync(Site site, TrackerInput input, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var booking = await db.Bookings.Include(b => b.Conversions).FirstOrDefaultAsync(b => b.BookingUid == input.Uid, ct);
        if (booking is null)
        {
            booking = new TrackedBooking { Id = Guid.NewGuid(), BookingUid = input.Uid, ClickId = input.ClickId, CreatedAt = now };
            db.Bookings.Add(booking);
        }
        booking.SiteId ??= site.Id;

        // Browser data is unverified: it only fills gaps and never overrides webhook data.
        booking.AffiliateId ??= input.AffiliateId;
        booking.Sub1 ??= input.Subs.ElementAtOrDefault(0);
        booking.Sub2 ??= input.Subs.ElementAtOrDefault(1);
        booking.Sub3 ??= input.Subs.ElementAtOrDefault(2);
        booking.Sub4 ??= input.Subs.ElementAtOrDefault(3);
        booking.Sub5 ??= input.Subs.ElementAtOrDefault(4);
        booking.Site ??= input.Site;
        booking.LandingUrl ??= input.LandingUrl;
        booking.Referrer ??= input.Referrer;
        booking.ClickedAt ??= input.ClickedAt;
        booking.Title ??= input.Title;
        booking.StartTime ??= input.StartTime;
        booking.EndTime ??= input.EndTime;
        if (!booking.WebhookConfirmed && input.Status is { } s) booking.Status = s;
        if (input.Status == BookingStatus.AwaitingPayment) booking.PaymentRequired = true;
        booking.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (db.Entry(booking).State == EntityState.Added)
        {
            // Two tracker calls raced for the same uid; the other one won.
            db.Entry(booking).State = EntityState.Detached;
            return await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingUid == input.Uid, ct);
        }

        // A webhook may have arrived before the browser call: apply any buffered ones now.
        var buffered = await db.WebhookEvents
            .Where(e => e.BookingUid == booking.BookingUid && e.TrackedBookingId == null)
            .OrderBy(e => e.ReceivedAt).ToListAsync(ct);
        foreach (var evt in buffered)
        {
            using var doc = JsonDocument.Parse(evt.Payload);
            var p = PayloadOf(doc.RootElement);
            Apply(booking, evt.Trigger, p, evt.ReceivedAt);
            ApplyTracking(booking, p);
            booking.SiteId = evt.SiteId ?? booking.SiteId; // the signed webhook knows the site for sure
            evt.TrackedBookingId = booking.Id;
        }

        await EvaluateAsync(booking, ct);
        await db.SaveChangesAsync(ct);
        if (booking.Conversions.Any(c => c.Status == ConversionStatus.Queued)) queue.Signal();
        return booking;
    }

    /// <summary>
    /// Applies a signed Zylux webhook from <paramref name="site"/>. A booking Zylux recorded a click ID
    /// for (payload.tracking) is tracked from the webhook alone, with no browser call needed.
    /// </summary>
    public async Task<bool> HandleWebhookAsync(Site site, string trigger, string rawBody, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var payload = PayloadOf(doc.RootElement);
        var uid = Str(payload, "uid");
        var now = DateTimeOffset.UtcNow;

        TrackedBooking? booking = null;
        if (uid is not null) booking = await db.Bookings.Include(b => b.Conversions).FirstOrDefaultAsync(b => b.BookingUid == uid, ct);

        // Reschedules may come with a new uid that points at the original booking.
        if (booking is null && trigger == "BOOKING_RESCHEDULED")
        {
            foreach (var key in RescheduleKeys)
            {
                var oldUid = Str(payload, key);
                if (oldUid is null) continue;
                booking = await db.Bookings.Include(b => b.Conversions).FirstOrDefaultAsync(b => b.BookingUid == oldUid, ct);
                if (booking is null) continue;
                if (uid is not null && uid != oldUid)
                {
                    booking.OriginalBookingUid ??= oldUid;
                    booking.BookingUid = uid;
                }
                break;
            }
        }

        // Not seen yet, but Zylux recorded the TrackCrest click ID on the booking: track it from here.
        var created = false;
        if (booking is null && uid is not null && UidRx().IsMatch(uid) && FirstTracking(payload, ClickKeys) is { } clickId && ClickRx().IsMatch(clickId))
        {
            booking = new TrackedBooking { Id = Guid.NewGuid(), BookingUid = uid, ClickId = clickId, SiteId = site.Id, CreatedAt = now, UpdatedAt = now };
            db.Bookings.Add(booking);
            created = true;
        }

        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(), Trigger = trigger, BookingUid = uid, Payload = rawBody, ReceivedAt = now,
            TrackedBookingId = booking?.Id, SiteId = site.Id,
        };
        db.WebhookEvents.Add(evt);

        if (booking is not null)
        {
            if (booking.SiteId is { } other && other != site.Id)
                log.LogWarning("Webhook for {Uid} came from site {Site} but the booking belongs to another site", uid, site.Key);
            booking.SiteId ??= site.Id;
            Apply(booking, trigger, payload, now);
            ApplyTracking(booking, payload);
            await EvaluateAsync(booking, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (created)
        {
            // The tracker call created the same booking a moment earlier: apply the webhook to that one.
            db.ChangeTracker.Clear();
            return await HandleWebhookAsync(site, trigger, rawBody, ct);
        }
        if (booking?.Conversions.Any(c => c.Status == ConversionStatus.Queued) == true) queue.Signal();
        log.LogInformation("Webhook {Trigger} site={Site} uid={Uid} matched={Matched} created={Created}", trigger, site.Key, uid, booking is not null, created);
        return booking is not null;
    }

    static readonly string[] ClickKeys = ["click_id", "clickid", "cid", "tc_cid"];
    static readonly string[] AffiliateKeys = ["aff_id", "affiliate_id", "aff"];

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{3,100}$")] private static partial Regex UidRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{4,128}$")] private static partial Regex ClickRx();

    /// <summary>Fills attribution gaps from the webhook's tracking object (Zylux Meet's record of the landing parameters).</summary>
    static void ApplyTracking(TrackedBooking b, JsonElement p)
    {
        if (!p.TryGetProperty("tracking", out var t) || t.ValueKind != JsonValueKind.Object) return;
        var all = new Dictionary<string, string>();
        foreach (var prop in t.EnumerateObject())
        {
            if (all.Count >= 20) break;
            var v = Str(t, prop.Name);
            if (v is not null && prop.Name.Length <= 40) all[prop.Name] = v.Length > 200 ? v[..200] : v;
        }
        if (all.Count == 0) return;
        b.Tracking = all;
        string? Cap(string? v) => v is null ? null : v.Length > 100 ? v[..100] : v;
        b.AffiliateId ??= Cap(FirstTracking(p, AffiliateKeys));
        b.Sub1 ??= Cap(Str(t, "sub1"));
        b.Sub2 ??= Cap(Str(t, "sub2"));
        b.Sub3 ??= Cap(Str(t, "sub3"));
        b.Sub4 ??= Cap(Str(t, "sub4"));
        b.Sub5 ??= Cap(Str(t, "sub5"));
    }

    static string? FirstTracking(JsonElement p, string[] keys)
    {
        if (!p.TryGetProperty("tracking", out var t) || t.ValueKind != JsonValueKind.Object) return null;
        foreach (var k in keys) if (Str(t, k) is { } v) return v;
        return null;
    }

    void Apply(TrackedBooking b, string trigger, JsonElement p, DateTimeOffset at)
    {
        if (!trigger.StartsWith("BOOKING_", StringComparison.Ordinal)) return;
        b.WebhookConfirmed = true;
        b.UpdatedAt = DateTimeOffset.UtcNow;

        b.Title = Str(p, "title") ?? b.Title;
        b.StartTime = Date(p, "startTime") ?? b.StartTime;
        b.EndTime = Date(p, "endTime") ?? b.EndTime;
        b.EventTypeId = Str(p, "eventTypeId") ?? b.EventTypeId;

        if (p.TryGetProperty("attendees", out var att) && att.ValueKind == JsonValueKind.Array && att.GetArrayLength() > 0)
        {
            var a = att[0];
            b.AttendeeName = Str(a, "name") ?? b.AttendeeName;
            b.AttendeeEmail = Str(a, "email") ?? b.AttendeeEmail;
            b.AttendeeTimeZone = Str(a, "timeZone") ?? b.AttendeeTimeZone;
            if (a.TryGetProperty("noShow", out var ns) && ns.ValueKind is JsonValueKind.True or JsonValueKind.False) b.NoShow = ns.GetBoolean();
        }
        if (p.TryGetProperty("organizer", out var org) && org.ValueKind == JsonValueKind.Object)
            b.OrganizerName = Str(org, "name") ?? b.OrganizerName;
        if (p.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object)
        {
            b.LocationType = Str(loc, "type") ?? b.LocationType;
            b.LocationUrl = Str(loc, "url") ?? b.LocationUrl;
        }
        if (p.TryGetProperty("noShow", out var noShow) && noShow.ValueKind is JsonValueKind.True or JsonValueKind.False)
            b.NoShow = noShow.GetBoolean();

        var paid = false;
        if (p.TryGetProperty("payment", out var pay) && pay.ValueKind == JsonValueKind.Object)
        {
            b.Amount = Dec(pay, "amount") ?? b.Amount;
            b.Currency = Str(pay, "currency")?.ToUpperInvariant() ?? b.Currency;
            b.PaymentStatus = Str(pay, "status") ?? b.PaymentStatus;
            b.PaymentProvider = Str(pay, "provider") ?? b.PaymentProvider;
            if (b.Amount > 0) b.PaymentRequired = true;
            paid = b.PaymentStatus?.ToLowerInvariant() is "paid" or "succeeded" or "success" or "completed";
        }

        BookingStatus? next = trigger switch
        {
            "BOOKING_CREATED" => ParseStatus(Str(p, "status")) ?? BookingStatus.Accepted,
            "BOOKING_REQUESTED" => BookingStatus.Pending,
            "BOOKING_PAID" => BookingStatus.Paid,
            "BOOKING_RESCHEDULED" => ParseStatus(Str(p, "status")),
            "BOOKING_CANCELLED" => BookingStatus.Cancelled,
            "BOOKING_REJECTED" => BookingStatus.Rejected,
            _ => null,
        };
        if (trigger == "BOOKING_PAID" || (paid && next is BookingStatus.Accepted or null))
        {
            next = BookingStatus.Paid;
            b.PaymentRequired = true;
            b.PaidAt ??= at;
        }

        // Webhooks can arrive out of order: never downgrade a paid booking unless it was cancelled/rejected.
        if (next is { } n && !(b.Status == BookingStatus.Paid && n is not (BookingStatus.Cancelled or BookingStatus.Rejected)))
            b.Status = n;
    }

    /// <summary>
    /// Creates the conversions the booking's site reports (lead, sale or both) and queues the ones that
    /// now qualify. A lead qualifies once the meeting is booked (accepted, awaiting payment or paid);
    /// a sale once it is paid. Sent, queued and failed conversions are never touched again.
    /// </summary>
    async Task EvaluateAsync(TrackedBooking b, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var site = b.SiteId is { } siteId ? await db.Sites.FindAsync([siteId], ct) : null;
        if (site is not null)
        {
            foreach (var kind in Enum.GetValues<ConversionKind>())
            {
                if (!site.Reports(kind) || b.Conversions.Any(c => c.Kind == kind)) continue;
                var c = new Conversion { Id = Guid.NewGuid(), TrackedBookingId = b.Id, Kind = kind, CreatedAt = now, UpdatedAt = now };
                // Added explicitly: with its key already set, EF would otherwise take it for an existing row.
                db.Conversions.Add(c);
                b.Conversions.Add(c);
            }
        }

        var cancelled = b.Status is BookingStatus.Cancelled or BookingStatus.Rejected;
        var confirmed = b.WebhookConfirmed || !tracking.Value.RequireWebhookConfirmation;
        foreach (var c in b.Conversions)
        {
            if (c.Status is ConversionStatus.Sent or ConversionStatus.Queued or ConversionStatus.Failed) continue;
            var next =
                cancelled ? ConversionStatus.NotEligible
                // A free meeting that's booked will never be paid for.
                : c.Kind == ConversionKind.Sale && b.Status == BookingStatus.Accepted && !b.PaymentRequired && b.WebhookConfirmed ? ConversionStatus.NotEligible
                : Qualifies(b, c.Kind) && confirmed ? ConversionStatus.Queued
                : ConversionStatus.Waiting;
            if (next == c.Status) continue;
            c.Status = next;
            c.NextAttemptAt = next == ConversionStatus.Queued ? now : null;
            c.UpdatedAt = now;
        }
        Summarize(b);
    }

    static bool Qualifies(TrackedBooking b, ConversionKind kind) => kind == ConversionKind.Sale
        ? b.Status == BookingStatus.Paid
        : b.Status is BookingStatus.Accepted or BookingStatus.AwaitingPayment or BookingStatus.Paid;

    /// <summary>Rolls the booking's conversions up into <see cref="TrackedBooking.ConversionStatus"/> for lists and filters.</summary>
    public static void Summarize(TrackedBooking b)
    {
        ConversionStatus[] priority = [ConversionStatus.Failed, ConversionStatus.Queued, ConversionStatus.Waiting, ConversionStatus.Sent, ConversionStatus.NotEligible];
        b.ConversionStatus = b.Conversions.Count == 0
            ? b.Status is BookingStatus.Cancelled or BookingStatus.Rejected ? ConversionStatus.NotEligible : ConversionStatus.Waiting
            : priority.First(p => b.Conversions.Any(c => c.Status == p));
        b.ConversionSentAt = b.Conversions.Max(c => c.SentAt);
    }

    public static BookingStatus? ParseStatus(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "accepted" or "confirmed" => BookingStatus.Accepted,
        "pending" => BookingStatus.Pending,
        "awaiting_payment" => BookingStatus.AwaitingPayment,
        "paid" => BookingStatus.Paid,
        "cancelled" or "canceled" => BookingStatus.Cancelled,
        "rejected" => BookingStatus.Rejected,
        _ => null,
    };

    static JsonElement PayloadOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object ? p : root;

    static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;

    static decimal? Dec(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
