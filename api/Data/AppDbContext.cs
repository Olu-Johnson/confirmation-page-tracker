using System.Text.Json;
using BookingTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BookingTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TrackedBooking> Bookings => Set<TrackedBooking>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<ConversionAttempt> ConversionAttempts => Set<ConversionAttempt>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Conversion> Conversions => Set<Conversion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TrackedBooking>(e =>
        {
            e.ToTable("tracked_bookings");
            e.HasIndex(x => x.BookingUid).IsUnique();
            e.HasIndex(x => x.ClickId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.ConversionStatus);

            e.Property(x => x.BookingUid).HasMaxLength(100);
            e.Property(x => x.OriginalBookingUid).HasMaxLength(100);
            e.Property(x => x.ClickId).HasMaxLength(128);
            e.Property(x => x.AffiliateId).HasMaxLength(100);
            foreach (var p in new[] { "Sub1", "Sub2", "Sub3", "Sub4", "Sub5" }) e.Property(p).HasMaxLength(100);
            e.Property(x => x.Site).HasMaxLength(200);
            e.HasIndex(x => x.SiteId);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Tracking).HasColumnType("jsonb").HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null),
                new ValueComparer<Dictionary<string, string>?>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v == null ? null : new Dictionary<string, string>(v)));
            e.Property(x => x.LandingUrl).HasMaxLength(1000);
            e.Property(x => x.Referrer).HasMaxLength(1000);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.ReconciliationNotes).HasMaxLength(2000);

            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ConversionStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ReconciliationStatus).HasConversion<string>().HasMaxLength(30);

            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.TrackedBookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attempts).WithOne().HasForeignKey(x => x.TrackedBookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Conversions).WithOne().HasForeignKey(x => x.TrackedBookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Site>(e =>
        {
            e.ToTable("sites");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.LeadPostbackUrl).HasMaxLength(2000);
            e.Property(x => x.SalePostbackUrl).HasMaxLength(2000);
            e.Property(x => x.WebhookSecret).HasMaxLength(200);
        });

        b.Entity<Conversion>(e =>
        {
            e.ToTable("conversions");
            e.HasIndex(x => new { x.TrackedBookingId, x.Kind }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.LastError).HasMaxLength(500);
        });

        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("webhook_events");
            e.HasIndex(x => x.BookingUid);
            e.HasIndex(x => x.ReceivedAt);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Trigger).HasMaxLength(60);
            e.Property(x => x.BookingUid).HasMaxLength(100);
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });

        b.Entity<ConversionAttempt>(e =>
        {
            e.ToTable("conversion_attempts");
            e.HasIndex(x => x.TrackedBookingId);
            e.HasOne<Conversion>().WithMany().HasForeignKey(x => x.ConversionId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Url).HasMaxLength(2000);
            e.Property(x => x.Response).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(500);
        });
    }
}
