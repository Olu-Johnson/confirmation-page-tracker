namespace BookingTracker.Api.Models;

/// <summary>Booking lifecycle as reported by Zylux Meet (webhooks are authoritative, the browser tracker only fills gaps).</summary>
public enum BookingStatus { Pending, AwaitingPayment, Accepted, Paid, Cancelled, Rejected }

/// <summary>State of the TrackCrest server-to-server postback for a booking.</summary>
public enum ConversionStatus
{
    Waiting,      // tracked, but not eligible yet (e.g. awaiting payment or webhook confirmation)
    Queued,       // eligible; the worker will (re)send it
    Sent,         // TrackCrest answered 2xx
    Failed,       // gave up after MaxAttempts; can be retried from the admin
    NotEligible   // cancelled/rejected before it qualified
}

/// <summary>Manual review state used when reconciling with the business owner.</summary>
public enum ReconciliationStatus { Unreviewed, Confirmed, Disputed }

/// <summary>A Zylux Meet booking made by a visitor who arrived through a TrackCrest tracking link.</summary>
public class TrackedBooking
{
    public Guid Id { get; set; }
    public string BookingUid { get; set; } = "";
    public string? OriginalBookingUid { get; set; }

    // Attribution (captured by tracker.js on the client's site)
    public string ClickId { get; set; } = "";
    public string? AffiliateId { get; set; }
    public string? Sub1 { get; set; }
    public string? Sub2 { get; set; }
    public string? Sub3 { get; set; }
    public string? Sub4 { get; set; }
    public string? Sub5 { get; set; }
    public string? Site { get; set; }
    public string? LandingUrl { get; set; }
    public string? Referrer { get; set; }
    public DateTimeOffset? ClickedAt { get; set; }

    // Booking details
    public string? Title { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public bool WebhookConfirmed { get; set; }
    public bool? NoShow { get; set; }
    public string? EventTypeId { get; set; }
    public string? AttendeeName { get; set; }
    public string? AttendeeEmail { get; set; }
    public string? AttendeeTimeZone { get; set; }
    public string? OrganizerName { get; set; }
    public string? LocationType { get; set; }
    public string? LocationUrl { get; set; }

    // Payment
    public bool PaymentRequired { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentProvider { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    // TrackCrest conversion
    public ConversionStatus ConversionStatus { get; set; } = ConversionStatus.Waiting;
    public int ConversionAttemptCount { get; set; }
    public DateTimeOffset? NextConversionAttemptAt { get; set; }
    public DateTimeOffset? ConversionSentAt { get; set; }
    public string? LastConversionError { get; set; }

    // Reconciliation
    public ReconciliationStatus ReconciliationStatus { get; set; } = ReconciliationStatus.Unreviewed;
    public string? ReconciliationNotes { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<WebhookEvent> Events { get; set; } = [];
    public List<ConversionAttempt> Attempts { get; set; } = [];
}

/// <summary>A Zylux webhook delivery. Unmatched ones (bookings not from a tracking link) are purged after a short window.</summary>
public class WebhookEvent
{
    public Guid Id { get; set; }
    public Guid? TrackedBookingId { get; set; }
    public string? BookingUid { get; set; }
    public string Trigger { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>One call to the TrackCrest postback URL.</summary>
public class ConversionAttempt
{
    public Guid Id { get; set; }
    public Guid TrackedBookingId { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public string Url { get; set; } = "";
    public int? HttpStatus { get; set; }
    public bool Success { get; set; }
    public string? Response { get; set; }
    public string? Error { get; set; }
    public bool Manual { get; set; }
}
