namespace BookingTracker.Api.Models;

/// <summary>Booking lifecycle as reported by Zylux Meet (webhooks are authoritative, the browser tracker only fills gaps).</summary>
public enum BookingStatus { Pending, AwaitingPayment, Accepted, Paid, Cancelled, Rejected }

/// <summary>State of one TrackCrest server-to-server postback (and, on a booking, the summary of its postbacks).</summary>
public enum ConversionStatus
{
    Waiting,      // tracked, but not eligible yet (e.g. awaiting payment or webhook confirmation)
    Queued,       // eligible; the worker will (re)send it
    Sent,         // TrackCrest answered 2xx
    Failed,       // gave up after MaxAttempts; can be retried from the admin
    NotEligible   // cancelled/rejected before it qualified
}

/// <summary>What a booking is reported to TrackCrest as.</summary>
public enum ConversionKind
{
    Lead, // a meeting was booked (free, or paid and waiting for / done with payment)
    Sale  // a paid meeting was paid for
}

/// <summary>Which conversions a site reports.</summary>
public enum TrackMode
{
    Lead, // every booked meeting, free or paid
    Sale, // paid meetings only, once paid
    Both  // a lead when booked, plus a sale when a paid meeting is paid
}

/// <summary>
/// A client website using Zylux Meet that is promoted through TrackCrest. Holds what to report
/// (lead / sale / both), where to send it, and how to recognise its webhooks and tracker calls.
/// </summary>
public class Site
{
    public Guid Id { get; set; }
    /// <summary>Short id used in the tracker script (data-site) and the webhook URL, e.g. "dept-habyyer".</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public TrackMode Mode { get; set; } = TrackMode.Both;
    /// <summary>TrackCrest postback for leads. Placeholders as in <see cref="Services.ConversionSender.BuildUrl"/>.</summary>
    public string? LeadPostbackUrl { get; set; }
    /// <summary>TrackCrest postback for sales.</summary>
    public string? SalePostbackUrl { get; set; }
    /// <summary>Same secret as the site's Zylux Meet webhook.</summary>
    public string WebhookSecret { get; set; } = "";
    /// <summary>Origins allowed to call the tracker endpoint for this site, e.g. https://dept.habyyer.com</summary>
    public List<string> AllowedOrigins { get; set; } = [];
    /// <summary>Look of this site's booking confirmation page, one of <see cref="ConfirmationThemes.All"/>.</summary>
    public string ConfirmationTheme { get; set; } = ConfirmationThemes.Default;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Reports(ConversionKind kind) => Mode == TrackMode.Both || (Mode == TrackMode.Lead ? kind == ConversionKind.Lead : kind == ConversionKind.Sale);
    public string? PostbackUrlFor(ConversionKind kind) => kind == ConversionKind.Lead ? LeadPostbackUrl : SalePostbackUrl;
}

/// <summary>Confirmation page themes. The web app (web/src/lib/themes.ts) has the same keys.</summary>
public static class ConfirmationThemes
{
    public const string Default = "aurora";
    public static readonly string[] All = ["aurora", "minimal", "ocean", "sunset", "noir"];
}

/// <summary>One postback to TrackCrest for a booking: its lead, or its sale.</summary>
public class Conversion
{
    public Guid Id { get; set; }
    public Guid TrackedBookingId { get; set; }
    public ConversionKind Kind { get; set; }
    public ConversionStatus Status { get; set; } = ConversionStatus.Waiting;
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Manual review state used when reconciling with the business owner.</summary>
public enum ReconciliationStatus { Unreviewed, Confirmed, Disputed }

/// <summary>Whether the affiliate has been paid for a booking. Set by the super admin only.</summary>
public enum AffiliatePaymentStatus { Pending, Paid, Rejected }

/// <summary>
/// A login for one site's team: they see only that site's bookings, read-only.
/// The super admin (ADMIN_USERNAME in the web app) is not stored here.
/// </summary>
public class SiteUser
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    /// <summary>Login name, stored lowercase.</summary>
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A Zylux Meet booking made by a visitor who arrived through a TrackCrest tracking link.</summary>
public class TrackedBooking
{
    public Guid Id { get; set; }
    public string BookingUid { get; set; } = "";
    public string? OriginalBookingUid { get; set; }
    /// <summary>The client site (from the signed webhook, else the tracker call).</summary>
    public Guid? SiteId { get; set; }

    // Attribution (captured by tracker.js on the client's site)
    public string ClickId { get; set; } = "";
    public string? AffiliateId { get; set; }
    public string? Sub1 { get; set; }
    public string? Sub2 { get; set; }
    public string? Sub3 { get; set; }
    public string? Sub4 { get; set; }
    public string? Sub5 { get; set; }
    /// <summary>Origin of the page the tracker ran on.</summary>
    public string? Site { get; set; }
    /// <summary>Every campaign/affiliate parameter Zylux Meet recorded on the booking (click_id, utm_*, sub1…).</summary>
    public Dictionary<string, string>? Tracking { get; set; }
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

    // TrackCrest conversions: summary of Conversions, for filtering and lists
    /// <summary>Failed, then Queued, then Waiting, then Sent, then NotEligible: the state that needs attention first.</summary>
    public ConversionStatus ConversionStatus { get; set; } = ConversionStatus.Waiting;
    /// <summary>When the latest postback was accepted.</summary>
    public DateTimeOffset? ConversionSentAt { get; set; }

    // Reconciliation
    public ReconciliationStatus ReconciliationStatus { get; set; } = ReconciliationStatus.Unreviewed;
    public string? ReconciliationNotes { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }

    // Affiliate payout (super admin only)
    public AffiliatePaymentStatus AffiliatePaymentStatus { get; set; } = AffiliatePaymentStatus.Pending;
    /// <summary>When the affiliate payment status was last set to Paid or Rejected; null while Pending.</summary>
    public DateTimeOffset? AffiliatePaymentDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<WebhookEvent> Events { get; set; } = [];
    public List<Conversion> Conversions { get; set; } = [];
    public List<ConversionAttempt> Attempts { get; set; } = [];
}

/// <summary>A Zylux webhook delivery. Unmatched ones (bookings not from a tracking link) are purged after a short window.</summary>
public class WebhookEvent
{
    public Guid Id { get; set; }
    public Guid? TrackedBookingId { get; set; }
    public Guid? SiteId { get; set; }
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
    public Guid? ConversionId { get; set; }
    public ConversionKind? Kind { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public string Url { get; set; } = "";
    public int? HttpStatus { get; set; }
    public bool Success { get; set; }
    public string? Response { get; set; }
    public string? Error { get; set; }
    public bool Manual { get; set; }
}
