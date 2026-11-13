namespace BookingTracker.Api;

public class TrackCrestOptions
{
    /// <summary>
    /// Legacy single-site setting: only used to create the "default" site the first time the API starts
    /// with no sites (as its sale postback). Postback URLs are now set per site in the admin.
    /// </summary>
    public string PostbackUrl { get; set; } = "";
    public int MaxAttempts { get; set; } = 6;
    public int TimeoutSeconds { get; set; } = 10;
}

public class ZyluxOptions
{
    /// <summary>Legacy single-site setting: seeds the "default" site's webhook secret. Now set per site in the admin.</summary>
    public string WebhookSecret { get; set; } = "";
}

public class TrackingOptions
{
    /// <summary>Legacy single-site setting: seeds the "default" site's allowed origins. Now set per site in the admin.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// When true (recommended) a conversion is only sent after a signed Zylux webhook confirms the booking,
    /// so a faked browser call can never create a payout.
    /// </summary>
    public bool RequireWebhookConfirmation { get; set; } = true;

    /// <summary>How long webhooks for non-tracked bookings are kept (to match a tracker call that arrives late).</summary>
    public int UnmatchedWebhookRetentionMinutes { get; set; } = 60;
}

public class AdminOptions
{
    /// <summary>Shared secret the Next.js admin sends in the X-Admin-Key header.</summary>
    public string ApiKey { get; set; } = "";
}
