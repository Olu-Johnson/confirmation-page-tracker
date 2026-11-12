namespace BookingTracker.Api;

public class TrackCrestOptions
{
    /// <summary>
    /// Postback URL from the TrackCrest dashboard. Placeholders: {click_id} {txid} {booking_uid}
    /// {amount} {currency} {status} {affiliate_id} {sub1}..{sub5}. Values are URL-encoded.
    /// </summary>
    public string PostbackUrl { get; set; } = "";
    public int MaxAttempts { get; set; } = 6;
    public int TimeoutSeconds { get; set; } = 10;
}

public class ZyluxOptions
{
    /// <summary>Same secret as in Zylux Meet → Settings → Developer → Webhooks.</summary>
    public string WebhookSecret { get; set; } = "";
}

public class TrackingOptions
{
    /// <summary>Sites allowed to call the tracker endpoint, e.g. https://dept.habyyer.com</summary>
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
