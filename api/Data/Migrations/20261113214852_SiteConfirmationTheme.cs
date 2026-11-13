using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteConfirmationTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmationTheme",
                table: "sites",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "aurora");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationTheme",
                table: "sites");
        }
    }
}
