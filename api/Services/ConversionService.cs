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

/// <summary>Calls the TrackCrest postback URL and records every attempt.</summary>
public class ConversionSender(
    AppDbContext db, IHttpClientFactory http, IOptions<TrackCrestOptions> tc, IOptions<TrackingOptions> tracking, ILogger<ConversionSender> log)
{
    static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6)];

    public async Task ProcessDueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.Bookings
            .Where(b => b.ConversionStatus == ConversionStatus.Queued && (b.NextConversionAttemptAt == null || b.NextConversionAttemptAt <= now))
            .OrderBy(b => b.NextConversionAttemptAt)
            .Take(25)
            .ToListAsync(ct);

        foreach (var booking in due)
        {
            await SendAsync(booking, manual: false, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SendAsync(TrackedBooking b, bool manual, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        b.UpdatedAt = now;

        if (!manual && b.Status is BookingStatus.Cancelled or BookingStatus.Rejected)
        {
            b.ConversionStatus = ConversionStatus.NotEligible;
            b.NextConversionAttemptAt = null;
            return;
        }

        var template = tc.Value.PostbackUrl;
        if (string.IsNullOrWhiteSpace(template))
        {
            // Not a TrackCrest failure: keep it queued so it goes out once the URL is configured.
            b.LastConversionError = "TrackCrest:PostbackUrl is not configured on the API.";
            b.NextConversionAttemptAt = now.AddMinutes(5);
            return;
        }

        var url = BuildUrl(template, b);
        var attempt = new ConversionAttempt { Id = Guid.NewGuid(), TrackedBookingId = b.Id, AttemptedAt = now, Url = url, Manual = manual };
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
        b.ConversionAttemptCount++;

        if (attempt.Success)
        {
            b.ConversionStatus = ConversionStatus.Sent;
            b.ConversionSentAt = now;
            b.NextConversionAttemptAt = null;
            b.LastConversionError = null;
            log.LogInformation("Conversion sent for {Uid}", b.BookingUid);
            return;
        }

        b.LastConversionError = attempt.Error ?? $"TrackCrest responded {attempt.HttpStatus}";
        if (b.ConversionAttemptCount >= tc.Value.MaxAttempts)
        {
            b.ConversionStatus = ConversionStatus.Failed;
            b.NextConversionAttemptAt = null;
        }
        else
        {
            b.ConversionStatus = ConversionStatus.Queued;
            b.NextConversionAttemptAt = now + Backoff[Math.Min(b.ConversionAttemptCount - 1, Backoff.Length - 1)];
        }
        log.LogWarning("Conversion attempt {N} failed for {Uid}: {Error}", b.ConversionAttemptCount, b.BookingUid, b.LastConversionError);
    }

    /// <summary>Removes webhooks for bookings that never came through a tracking link.</summary>
    public Task<int> PurgeUnmatchedWebhooksAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-tracking.Value.UnmatchedWebhookRetentionMinutes);
        return db.WebhookEvents.Where(e => e.TrackedBookingId == null && e.ReceivedAt < cutoff).ExecuteDeleteAsync(ct);
    }

    public static string BuildUrl(string template, TrackedBooking b)
    {
        string E(string? v) => Uri.EscapeDataString(v ?? "");
        return template
            .Replace("{click_id}", E(b.ClickId))
            .Replace("{txid}", E(b.BookingUid))
            .Replace("{booking_uid}", E(b.BookingUid))
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
