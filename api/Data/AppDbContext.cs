using BookingTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TrackedBooking> Bookings => Set<TrackedBooking>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<ConversionAttempt> ConversionAttempts => Set<ConversionAttempt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TrackedBooking>(e =>
        {
            e.ToTable("tracked_bookings");
            e.HasIndex(x => x.BookingUid).IsUnique();
            e.HasIndex(x => x.ClickId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.ConversionStatus, x.NextConversionAttemptAt });

            e.Property(x => x.BookingUid).HasMaxLength(100);
            e.Property(x => x.OriginalBookingUid).HasMaxLength(100);
            e.Property(x => x.ClickId).HasMaxLength(128);
            e.Property(x => x.AffiliateId).HasMaxLength(100);
            foreach (var p in new[] { "Sub1", "Sub2", "Sub3", "Sub4", "Sub5" }) e.Property(p).HasMaxLength(100);
            e.Property(x => x.Site).HasMaxLength(200);
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
        });

        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("webhook_events");
            e.HasIndex(x => x.BookingUid);
            e.HasIndex(x => x.ReceivedAt);
            e.Property(x => x.Trigger).HasMaxLength(60);
            e.Property(x => x.BookingUid).HasMaxLength(100);
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });

        b.Entity<ConversionAttempt>(e =>
        {
            e.ToTable("conversion_attempts");
            e.HasIndex(x => x.TrackedBookingId);
            e.Property(x => x.Url).HasMaxLength(2000);
            e.Property(x => x.Response).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(500);
        });
    }
}
