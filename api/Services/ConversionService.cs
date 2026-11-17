using System.Globalization;
using System.Threading.Channels;
using BookingTracker.Api.Data;
using BookingTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingTracker.Api.Services;

/// <summary>Wakes the worker as soon as a conversion is queued (it also polls every 30s).</summary>
public class ConversionQueue
{
    readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Signal() => _signals.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _signals.Reader.WaitToReadAsync(cts.Token);
            _signals.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

/// <summary>Calls the site's TrackCrest postback URL for each lead/sale and records every attempt.</summary>
public class ConversionSender(
    AppDbContext db, IHttpClientFactory http, IOptions<TrackCrestOptions> tc, IOptions<TrackingOptions> tracking, ILogger<ConversionSender> log)
{
    static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6)];

    public async Task ProcessDueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.Conversions
            .Where(c => c.Status == ConversionStatus.Queued && (c.NextAttemptAt == null || c.NextAttemptAt <= now))
            .OrderBy(c => c.NextAttemptAt)
            .Take(25)
            .ToListAsync(ct);

        foreach (var conversion in due)
        {
            // Loads the booking with all its conversions (this one is already tracked, so it's the same instance).
            var booking = await db.Bookings.Include(b => b.Conversions).FirstAsync(b => b.Id == conversion.TrackedBookingId, ct);
            await SendAsync(booking, conversion, manual: false, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Sends one conversion. Returns an error message when nothing could be sent (no site or no URL).</summary>
    public async Task<string?> SendAsync(TrackedBooking b, Conversion c, bool manual, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        b.UpdatedAt = now;
        c.UpdatedAt = now;
        try
        {
            if (!manual && b.Status is BookingStatus.Cancelled or BookingStatus.Rejected)
            {
                c.Status = ConversionStatus.NotEligible;
                c.NextAttemptAt = null;
                return null;
            }

            var site = b.SiteId is { } siteId ? await db.Sites.FindAsync([siteId], ct) : null;
            var template = site?.PostbackUrlFor(c.Kind);
            if (string.IsNullOrWhiteSpace(template))
            {
                // Not a TrackCrest failure: keep it queued so it goes out once the URL is set.
                c.LastError = site is null
                    ? "This booking isn't linked to a site."
                    : $"The {c.Kind.ToString().ToLowerInvariant()} postback URL isn't set for site \"{site.Key}\" (Admin → Sites).";
                if (c.Status == ConversionStatus.Queued) c.NextAttemptAt = now.AddMinutes(5);
                return c.LastError;
            }

            var url = BuildUrl(template, b, c.Kind);
            var attempt = new ConversionAttempt
            {
                Id = Guid.NewGuid(), TrackedBookingId = b.Id, ConversionId = c.Id, Kind = c.Kind, AttemptedAt = now, Url = url, Manual = manual,
            };
            try
            {
                var client = http.CreateClient("trackcrest");
                using var resp = await client.GetAsync(url, ct);
                attempt.HttpStatus = (int)resp.StatusCode;
                attempt.Success = resp.IsSuccessStatusCode;
                var body = await resp.Content.ReadAsStringAsync(ct);
                attempt.Response = body.Length > 500 ? body[..500] : body;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                attempt.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            }

            db.ConversionAttempts.Add(attempt);
            c.AttemptCount++;

            if (attempt.Success)
            {
                c.Status = ConversionStatus.Sent;
                c.SentAt = now;
                c.NextAttemptAt = null;
                c.LastError = null;
                log.LogInformation("{Kind} conversion sent for {Uid}", c.Kind, b.BookingUid);
                return null;
            }

            c.LastError = attempt.Error ?? $"TrackCrest responded {attempt.HttpStatus}";
            if (c.AttemptCount >= tc.Value.MaxAttempts)
            {
                c.Status = ConversionStatus.Failed;
                c.NextAttemptAt = null;
            }
            else
            {
                c.Status = ConversionStatus.Queued;
                c.NextAttemptAt = now + Backoff[Math.Min(c.AttemptCount - 1, Backoff.Length - 1)];
            }
            log.LogWarning("{Kind} conversion attempt {N} failed for {Uid}: {Error}", c.Kind, c.AttemptCount, b.BookingUid, c.LastError);
            return c.LastError;
        }
        finally
        {
            BookingEventService.Summarize(b);
        }
    }

    /// <summary>Removes webhooks for bookings that never came through a tracking link.</summary>
    public Task<int> PurgeUnmatchedWebhooksAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-tracking.Value.UnmatchedWebhookRetentionMinutes);
        return db.WebhookEvents.Where(e => e.TrackedBookingId == null && e.ReceivedAt < cutoff).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Fills the postback URL's placeholders (URL-encoded): {click_id} {txid}/{booking_uid} (the Zylux booking uid)
    /// {conversion_id} (uid-lead / uid-sale, unique per postback) {event} (lead / sale) {amount} {currency}
    /// {status} (booking status) {affiliate_id} {sub1}..{sub5}.
    /// </summary>
    public static string BuildUrl(string template, TrackedBooking b, ConversionKind kind)
    {
        string E(string? v) => Uri.EscapeDataString(v ?? "");
        var ev = kind.ToString().ToLowerInvariant();
        return template
            .Replace("{click_id}", E(b.ClickId))
            .Replace("{txid}", E(b.BookingUid))
            .Replace("{booking_uid}", E(b.BookingUid))
            .Replace("{conversion_id}", E($"{b.BookingUid}-{ev}"))
            .Replace("{event}", E(ev))
            .Replace("{amount}", E(b.Amount?.ToString("0.##", CultureInfo.InvariantCulture)))
            .Replace("{currency}", E(b.Currency))
            .Replace("{status}", E(b.Status.ToString().ToLowerInvariant()))
            .Replace("{affiliate_id}", E(b.AffiliateId))
            .Replace("{sub1}", E(b.Sub1)).Replace("{sub2}", E(b.Sub2)).Replace("{sub3}", E(b.Sub3))
            .Replace("{sub4}", E(b.Sub4)).Replace("{sub5}", E(b.Sub5));
    }
}

/// <summary>Sends queued conversions, retries failures with backoff, and purges unmatched webhooks.</summary>
public class ConversionWorker(IServiceScopeFactory scopes, ConversionQueue queue, ILogger<ConversionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastPurge = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ConversionSender>();
                await sender.ProcessDueAsync(ct);
                if (DateTimeOffset.UtcNow - lastPurge > TimeSpan.FromMinutes(10))
                {
                    var n = await sender.PurgeUnmatchedWebhooksAsync(ct);
                    if (n > 0) log.LogInformation("Purged {Count} unmatched webhook events", n);
                    lastPurge = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogError(ex, "Conversion worker loop failed");
            }
            await queue.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
    }
}
