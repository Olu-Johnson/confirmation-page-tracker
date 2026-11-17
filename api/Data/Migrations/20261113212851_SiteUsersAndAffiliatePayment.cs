using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteUsersAndAffiliatePayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AffiliatePaymentDate",
                table: "tracked_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AffiliatePaymentStatus",
                table: "tracked_bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending"); // existing bookings start as pending

            migrationBuilder.CreateTable(
                name: "site_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_site_users_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tracked_bookings_AffiliatePaymentStatus",
                table: "tracked_bookings",
                column: "AffiliatePaymentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_site_users_Email",
                table: "site_users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_site_users_SiteId",
                table: "site_users",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_users");

            migrationBuilder.DropIndex(
                name: "IX_tracked_bookings_AffiliatePaymentStatus",
                table: "tracked_bookings");

            migrationBuilder.DropColumn(
                name: "AffiliatePaymentDate",
                table: "tracked_bookings");

            migrationBuilder.DropColumn(
                name: "AffiliatePaymentStatus",
                table: "tracked_bookings");
        }
    }
}
