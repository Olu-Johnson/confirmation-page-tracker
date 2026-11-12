using System.Globalization;
using System.Text.Json;
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
public class BookingEventService(
    AppDbContext db, ConversionQueue queue, IOptions<TrackingOptions> tracking, ILogger<BookingEventService> log)
{
    static readonly string[] RescheduleKeys = ["rescheduledFromUid", "fromReschedule", "originalBookingUid", "rescheduleUid", "rescheduledFrom"];

    public async Task<TrackedBooking> RecordTrackerAsync(TrackerInput input, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.BookingUid == input.Uid, ct);
        if (booking is null)
        {
            booking = new TrackedBooking { Id = Guid.NewGuid(), BookingUid = input.Uid, ClickId = input.ClickId, CreatedAt = now };
            db.Bookings.Add(booking);
        }

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
            return await db.Bookings.FirstAsync(b => b.BookingUid == input.Uid, ct);
        }

        // A webhook may have arrived before the browser call: apply any buffered ones now.
        var buffered = await db.WebhookEvents
            .Where(e => e.BookingUid == booking.BookingUid && e.TrackedBookingId == null)
            .OrderBy(e => e.ReceivedAt).ToListAsync(ct);
        foreach (var evt in buffered)
        {
            using var doc = JsonDocument.Parse(evt.Payload);
            Apply(booking, evt.Trigger, PayloadOf(doc.RootElement), evt.ReceivedAt);
            evt.TrackedBookingId = booking.Id;
        }

        Evaluate(booking);
        await db.SaveChangesAsync(ct);
        if (booking.ConversionStatus == ConversionStatus.Queued) queue.Signal();
        return booking;
    }

    public async Task<bool> HandleWebhookAsync(string trigger, string rawBody, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var payload = PayloadOf(doc.RootElement);
        var uid = Str(payload, "uid");
        var now = DateTimeOffset.UtcNow;

        TrackedBooking? booking = null;
        if (uid is not null) booking = await db.Bookings.FirstOrDefaultAsync(b => b.BookingUid == uid, ct);

        // Reschedules may come with a new uid that points at the original booking.
        if (booking is null && trigger == "BOOKING_RESCHEDULED")
        {
            foreach (var key in RescheduleKeys)
            {
                var oldUid = Str(payload, key);
                if (oldUid is null) continue;
                booking = await db.Bookings.FirstOrDefaultAsync(b => b.BookingUid == oldUid, ct);
                if (booking is null) continue;
                if (uid is not null && uid != oldUid)
                {
                    booking.OriginalBookingUid ??= oldUid;
                    booking.BookingUid = uid;
                }
                break;
            }
        }

        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(), Trigger = trigger, BookingUid = uid, Payload = rawBody, ReceivedAt = now,
            TrackedBookingId = booking?.Id,
        };
        db.WebhookEvents.Add(evt);

        if (booking is not null)
        {
            Apply(booking, trigger, payload, now);
            Evaluate(booking);
        }

        await db.SaveChangesAsync(ct);
        if (booking?.ConversionStatus == ConversionStatus.Queued) queue.Signal();
        log.LogInformation("Webhook {Trigger} uid={Uid} matched={Matched}", trigger, uid, booking is not null);
        return booking is not null;
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

    /// <summary>Decides whether the booking should now be reported to TrackCrest.</summary>
    void Evaluate(TrackedBooking b)
    {
        if (b.ConversionStatus is ConversionStatus.Sent or ConversionStatus.Queued or ConversionStatus.Failed) return;
        if (b.Status is BookingStatus.Cancelled or BookingStatus.Rejected)
        {
            b.ConversionStatus = ConversionStatus.NotEligible;
            return;
        }
        var eligible = b.Status == BookingStatus.Paid || (b.Status == BookingStatus.Accepted && !b.PaymentRequired);
        if (tracking.Value.RequireWebhookConfirmation && !b.WebhookConfirmed) eligible = false;

        if (eligible)
        {
            b.ConversionStatus = ConversionStatus.Queued;
            b.NextConversionAttemptAt = DateTimeOffset.UtcNow;
        }
        else b.ConversionStatus = ConversionStatus.Waiting;
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
