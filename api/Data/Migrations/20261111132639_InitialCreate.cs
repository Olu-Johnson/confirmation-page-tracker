using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tracked_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingUid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalBookingUid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClickId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AffiliateId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sub1 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sub2 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sub3 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sub4 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sub5 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Site = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LandingUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Referrer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClickedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WebhookConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    NoShow = table.Column<bool>(type: "boolean", nullable: true),
                    EventTypeId = table.Column<string>(type: "text", nullable: true),
                    AttendeeName = table.Column<string>(type: "text", nullable: true),
                    AttendeeEmail = table.Column<string>(type: "text", nullable: true),
                    AttendeeTimeZone = table.Column<string>(type: "text", nullable: true),
                    OrganizerName = table.Column<string>(type: "text", nullable: true),
                    LocationType = table.Column<string>(type: "text", nullable: true),
                    LocationUrl = table.Column<string>(type: "text", nullable: true),
                    PaymentRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PaymentStatus = table.Column<string>(type: "text", nullable: true),
                    PaymentProvider = table.Column<string>(type: "text", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConversionStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConversionAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextConversionAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConversionSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastConversionError = table.Column<string>(type: "text", nullable: true),
                    ReconciliationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReconciliationNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracked_bookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversion_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackedBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Response = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Manual = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversion_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversion_attempts_tracked_bookings_TrackedBookingId",
                        column: x => x.TrackedBookingId,
                        principalTable: "tracked_bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackedBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingUid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Trigger = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_webhook_events_tracked_bookings_TrackedBookingId",
                        column: x => x.TrackedBookingId,
                        principalTable: "tracked_bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversion_attempts_TrackedBookingId",
                table: "conversion_attempts",
                column: "TrackedBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_BookingUid",
                table: "tracked_bookings",
                column: "BookingUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_ClickId",
                table: "tracked_bookings",
                column: "ClickId");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_ConversionStatus_NextConversionAttemptAt",
                table: "tracked_bookings",
                columns: new[] { "ConversionStatus", "NextConversionAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_CreatedAt",
                table: "tracked_bookings",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_BookingUid",
                table: "webhook_events",
                column: "BookingUid");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_ReceivedAt",
                table: "webhook_events",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_TrackedBookingId",
                table: "webhook_events",
                column: "TrackedBookingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversion_attempts");

            migrationBuilder.DropTable(
                name: "webhook_events");

            migrationBuilder.DropTable(
                name: "tracked_bookings");
        }
    }
}
