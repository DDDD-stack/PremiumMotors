using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <inheritdoc />
    public partial class RecordTermsAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TermsAcceptedUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "Users");
        }
    }
}
