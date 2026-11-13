using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SitesAndConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tracked_bookings_ConversionStatus_NextConversionAttemptAt",
                table: "tracked_bookings");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "tracked_bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tracking",
                table: "tracked_bookings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversionId",
                table: "conversion_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "conversion_attempts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackedBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversions_tracked_bookings_TrackedBookingId",
                        column: x => x.TrackedBookingId,
                        principalTable: "tracked_bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LeadPostbackUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SalePostbackUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllowedOrigins = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.Id);
                });

            // Each booking's single conversion becomes a sale (paid meetings) or a lead (free ones),
            // keeping its status and attempts, before the old columns go.
            migrationBuilder.Sql("""
                INSERT INTO conversions ("Id", "TrackedBookingId", "Kind", "Status", "AttemptCount", "NextAttemptAt", "SentAt", "LastError", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), b."Id", CASE WHEN b."PaymentRequired" THEN 'Sale' ELSE 'Lead' END, b."ConversionStatus",
                       b."ConversionAttemptCount", b."NextConversionAttemptAt", b."ConversionSentAt", b."LastConversionError", b."CreatedAt", b."UpdatedAt"
                FROM tracked_bookings b;

                UPDATE conversion_attempts a
                SET "ConversionId" = c."Id", "Kind" = c."Kind"
                FROM conversions c
                WHERE c."TrackedBookingId" = a."TrackedBookingId";
                """);

            migrationBuilder.DropColumn(
                name: "ConversionAttemptCount",
                table: "tracked_bookings");

            migrationBuilder.DropColumn(
                name: "LastConversionError",
                table: "tracked_bookings");

            migrationBuilder.DropColumn(
                name: "NextConversionAttemptAt",
                table: "tracked_bookings");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_SiteId",
                table: "webhook_events",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_ConversionStatus",
                table: "tracked_bookings",
                column: "ConversionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_SiteId",
                table: "tracked_bookings",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_conversion_attempts_ConversionId",
                table: "conversion_attempts",
                column: "ConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_conversions_Status_NextAttemptAt",
                table: "conversions",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversions_TrackedBookingId_Kind",
                table: "conversions",
                columns: new[] { "TrackedBookingId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sites_Key",
                table: "sites",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_conversion_attempts_conversions_ConversionId",
                table: "conversion_attempts",
                column: "ConversionId",
                principalTable: "conversions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tracked_bookings_sites_SiteId",
                table: "tracked_bookings",
                column: "SiteId",
                principalTable: "sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_webhook_events_sites_SiteId",
                table: "webhook_events",
                column: "SiteId",
                principalTable: "sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversion_attempts_conversions_ConversionId",
                table: "conversion_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_tracked_bookings_sites_SiteId",
                table: "tracked_bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_webhook_events_sites_SiteId",
                table: "webhook_events");

            migrationBuilder.DropTable(
                name: "conversions");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropIndex(
                name: "IX_webhook_events_SiteId",
                table: "webhook_events");

            migrationBuilder.DropIndex(
                name: "IX_tracked_bookings_ConversionStatus",
                table: "tracked_bookings");

            migrationBuilder.DropIndex(
                name: "IX_tracked_bookings_SiteId",
                table: "tracked_bookings");

            migrationBuilder.DropIndex(
                name: "IX_conversion_attempts_ConversionId",
                table: "conversion_attempts");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "tracked_bookings");

            migrationBuilder.DropColumn(
                name: "Tracking",
                table: "tracked_bookings");

            migrationBuilder.DropColumn(
                name: "ConversionId",
                table: "conversion_attempts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "conversion_attempts");

            migrationBuilder.AddColumn<int>(
                name: "ConversionAttemptCount",
                table: "tracked_bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastConversionError",
                table: "tracked_bookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextConversionAttemptAt",
                table: "tracked_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_ConversionStatus_NextConversionAttemptAt",
                table: "tracked_bookings",
                columns: new[] { "ConversionStatus", "NextConversionAttemptAt" });
        }
    }
}
