using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <inheritdoc />
    public partial class StableUserIdsAndRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginUtc",
                table: "Users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "UserFavoriteCars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                table: "UserFavoriteCars",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "Cars",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SoldToUserId",
                table: "Cars",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BidderId",
                table: "Bids",
                type: "integer",
                nullable: true);

            // ----------------------------------------------------------------
            // Backfill the new id columns from the username strings that used to
            // carry identity, then drop the favourites Username column. This runs
            // before the new primary key is applied so no saved listing is lost.
            // On a fresh database every statement is a harmless no-op.
            // ----------------------------------------------------------------
            migrationBuilder.Sql(@"
                UPDATE ""UserFavoriteCars"" f
                   SET ""UserId"" = u.""Id""
                  FROM ""Users"" u
                 WHERE lower(u.""Username"") = lower(f.""Username"");");

            // Favourites whose username no longer matches an account cannot be attributed
            // to anyone, and would otherwise all collide on the new (UserId, CarId) key.
            migrationBuilder.Sql(@"DELETE FROM ""UserFavoriteCars"" WHERE ""UserId"" = 0;");

            migrationBuilder.Sql(@"
                UPDATE ""UserFavoriteCars""
                   SET ""CreatedUtc"" = (now() AT TIME ZONE 'utc')
                 WHERE ""CreatedUtc"" < TIMESTAMP '1900-01-01';");

            migrationBuilder.Sql(@"
                UPDATE ""Cars"" c
                   SET ""OwnerId"" = u.""Id""
                  FROM ""Users"" u
                 WHERE c.""OwnerUsername"" IS NOT NULL
                   AND lower(u.""Username"") = lower(c.""OwnerUsername"");");

            migrationBuilder.Sql(@"
                UPDATE ""Cars"" c
                   SET ""SoldToUserId"" = u.""Id""
                  FROM ""Users"" u
                 WHERE c.""SoldTo"" IS NOT NULL
                   AND lower(u.""Username"") = lower(c.""SoldTo"");");

            migrationBuilder.Sql(@"
                UPDATE ""Bids"" b
                   SET ""BidderId"" = u.""Id""
                  FROM ""Users"" u
                 WHERE lower(u.""Username"") = lower(b.""BidderName"");");

            migrationBuilder.Sql(@"
                UPDATE ""Payments"" p
                   SET ""UserId"" = u.""Id""
                  FROM ""Users"" u
                 WHERE lower(u.""Username"") = lower(p.""Username"");");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "UserFavoriteCars");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars",
                columns: new[] { "UserId", "CarId" });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReplacedByTokenId = table.Column<int>(type: "integer", nullable: true),
                    Device = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFavoriteCars_CarId",
                table: "UserFavoriteCars",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId",
                table: "Payments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cars_IsPublished",
                table: "Cars",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_Cars_OwnerId",
                table: "Cars",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Cars_SoldToUserId",
                table: "Cars",
                column: "SoldToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_BidderId",
                table: "Bids",
                column: "BidderId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedUtc",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Bids_Users_BidderId",
                table: "Bids",
                column: "BidderId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Cars_Users_OwnerId",
                table: "Cars",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Cars_Users_SoldToUserId",
                table: "Cars",
                column: "SoldToUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_UserId",
                table: "Payments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UserFavoriteCars_Cars_CarId",
                table: "UserFavoriteCars",
                column: "CarId",
                principalTable: "Cars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserFavoriteCars_Users_UserId",
                table: "UserFavoriteCars",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bids_Users_BidderId",
                table: "Bids");

            migrationBuilder.DropForeignKey(
                name: "FK_Cars_Users_OwnerId",
                table: "Cars");

            migrationBuilder.DropForeignKey(
                name: "FK_Cars_Users_SoldToUserId",
                table: "Cars");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_UserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserFavoriteCars_Cars_CarId",
                table: "UserFavoriteCars");

            migrationBuilder.DropForeignKey(
                name: "FK_UserFavoriteCars_Users_UserId",
                table: "UserFavoriteCars");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars");

            migrationBuilder.DropIndex(
                name: "IX_UserFavoriteCars_CarId",
                table: "UserFavoriteCars");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Cars_IsPublished",
                table: "Cars");

            migrationBuilder.DropIndex(
                name: "IX_Cars_OwnerId",
                table: "Cars");

            migrationBuilder.DropIndex(
                name: "IX_Cars_SoldToUserId",
                table: "Cars");

            migrationBuilder.DropIndex(
                name: "IX_Bids_BidderId",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLoginUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                table: "UserFavoriteCars");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "SoldToUserId",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "BidderId",
                table: "Bids");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "UserFavoriteCars",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Restore the username-keyed rows before the id column goes away, so reverting
            // this migration does not collapse every favourite onto an empty username.
            migrationBuilder.Sql(@"
                UPDATE ""UserFavoriteCars"" f
                   SET ""Username"" = u.""Username""
                  FROM ""Users"" u
                 WHERE u.""Id"" = f.""UserId"";");

            migrationBuilder.Sql(@"DELETE FROM ""UserFavoriteCars"" WHERE ""Username"" = '';");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UserFavoriteCars");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserFavoriteCars",
                table: "UserFavoriteCars",
                columns: new[] { "Username", "CarId" });
        }
    }
}
