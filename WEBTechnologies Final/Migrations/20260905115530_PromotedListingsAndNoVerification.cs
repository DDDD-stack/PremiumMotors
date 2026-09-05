using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <inheritdoc />
    public partial class PromotedListingsAndNoVerification : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// EF warned that this may lose data. It does not. Both dropped columns were dead:
        /// nothing in the codebase ever assigned either of them, so every row holds the
        /// scaffolded default of false. Checked by grep across the whole solution before
        /// generating this, and Down() restores both with the same default.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SellerVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "Dealerships");

            migrationBuilder.AddColumn<DateTime>(
                name: "PromotedUntilUtc",
                table: "Cars",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromotionTier",
                table: "Cars",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromotedUntilUtc",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "PromotionTier",
                table: "Cars");

            migrationBuilder.AddColumn<bool>(
                name: "SellerVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "Dealerships",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
