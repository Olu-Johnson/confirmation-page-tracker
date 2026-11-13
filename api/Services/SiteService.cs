using BookingTracker.Api.Data;
using BookingTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingTracker.Api.Services;

/// <summary>
/// The origins of every active site, for CORS and the tracker endpoint's Origin check. Loaded at
/// startup and reloaded whenever a site changes (the API runs as a single instance).
/// </summary>
public class SiteOriginCache
{
    volatile HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string? origin) => NormalizeOrigin(origin) is { } o && _origins.Contains(o);

    public async Task RefreshAsync(AppDbContext db, CancellationToken ct)
    {
        var lists = await db.Sites.AsNoTracking().Where(s => s.Active).Select(s => s.AllowedOrigins).ToListAsync(ct);
        _origins = new HashSet<string>(lists.SelectMany(x => x).Select(NormalizeOrigin).OfType<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>"https://Example.com/path/" → "https://example.com"; null for anything that isn't an http(s) origin.</summary>
    public static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return null;
        return u.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}

/// <summary>Finds the site a tracker call or webhook belongs to.</summary>
public class SiteService(AppDbContext db)
{
    public Task<Site?> ByKeyAsync(string key, CancellationToken ct) =>
        db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key.ToLower() && s.Active, ct);

    /// <summary>The active site whose allowed origins include <paramref name="origin"/> (first created wins).</summary>
    public async Task<Site?> ByOriginAsync(string? origin, CancellationToken ct)
    {
        var o = SiteOriginCache.NormalizeOrigin(origin);
        if (o is null) return null;
        var sites = await db.Sites.AsNoTracking().Where(s => s.Active).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        return sites.FirstOrDefault(s => s.AllowedOrigins.Any(a => string.Equals(SiteOriginCache.NormalizeOrigin(a), o, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<List<Site>> ActiveAsync(CancellationToken ct) =>
        db.Sites.AsNoTracking().Where(s => s.Active && s.WebhookSecret != "").OrderBy(s => s.CreatedAt).ToListAsync(ct);

    /// <summary>
    /// First start after upgrading from the single-site version: turns the old settings
    /// (TrackCrest:PostbackUrl, Zylux:WebhookSecret, Tracking:AllowedOrigins) into a site called
    /// "default" and links existing bookings to it. Its lead postback is left empty to be set in the admin.
    /// </summary>
    public static async Task SeedDefaultAsync(
        AppDbContext db, IOptions<TrackCrestOptions> tc, IOptions<ZyluxOptions> zylux, IOptions<TrackingOptions> tracking, ILogger log, CancellationToken ct)
    {
        if (await db.Sites.AnyAsync(ct)) return;
        var postback = tc.Value.PostbackUrl?.Trim() ?? "";
        var secret = zylux.Value.WebhookSecret?.Trim() ?? "";
        var origins = tracking.Value.AllowedOrigins.Select(SiteOriginCache.NormalizeOrigin).OfType<string>().Distinct().ToList();
        if (postback == "" && secret == "" && origins.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var site = new Site
        {
            Id = Guid.NewGuid(), Key = "default", Name = "Default site", Mode = TrackMode.Both,
            SalePostbackUrl = postback == "" ? null : postback, WebhookSecret = secret, AllowedOrigins = origins,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        await db.Bookings.Where(b => b.SiteId == null).ExecuteUpdateAsync(u => u.SetProperty(b => b.SiteId, site.Id), ct);
        await db.WebhookEvents.Where(e => e.SiteId == null).ExecuteUpdateAsync(u => u.SetProperty(e => e.SiteId, site.Id), ct);
        log.LogInformation("Created the \"default\" site from the old single-site settings. Set its lead postback URL in Admin → Sites.");
    }
}
