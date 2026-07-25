using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
        public partial class SyncMissingCarAndBidColumns : Migration
    {
                protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UserFavoriteCars");

            migrationBuilder.DropColumn(
                name: "PlacedUtc",
                table: "Bids");

            migrationBuilder.AlterColumn<DateTime>(
                name: "RegisteredUtc",
                table: "Users",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "UserFavoriteCars",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedUtc",
                table: "Cars",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AuctionEnd",
                table: "Cars",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                table: "Bids",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars",
                columns: new[] { "Username", "CarId" });
        }

                protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "UserFavoriteCars");

            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                table: "Bids");

            migrationBuilder.AlterColumn<DateTime>(
                name: "RegisteredUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "UserFavoriteCars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedUtc",
                table: "Cars",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AuctionEnd",
                table: "Cars",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlacedUtc",
                table: "Bids",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars",
                columns: new[] { "UserId", "CarId" });
        }
    }
}
