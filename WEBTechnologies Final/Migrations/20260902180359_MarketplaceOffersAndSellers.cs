using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <summary>
    /// Auction to marketplace.
    ///
    /// HAND-EDITED. The scaffolded version did two destructive things that had to be undone:
    ///   1. It dropped the Bids table and created Offers from scratch, which would have
    ///      deleted every offer ever made. Bids is RENAMED to Offers here instead.
    ///   2. It matched the dropped bool IsSold to the new bool HasAccidentHistory and emitted
    ///      a RenameColumn, which would have marked every sold car as accident-damaged.
    ///
    /// Column order matters below: Cars.Status is backfilled from IsPublished/IsSold, and the
    /// accepted offer is reconstructed from the old winning bid, before those columns go away.
    /// </summary>
    public partial class MarketplaceOffersAndSellers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- Users: seller capability ----------

            migrationBuilder.AddColumn<bool>(
                name: "IsSeller", table: "Users", type: "boolean", nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerSinceUtc", table: "Users", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellerType", table: "Users", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SellerDisplayName", table: "Users", type: "character varying(80)",
                maxLength: 80, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerLocation", table: "Users", type: "character varying(80)",
                maxLength: 80, nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SellerVerified", table: "Users", type: "boolean", nullable: false, defaultValue: false);

            migrationBuilder.CreateIndex(name: "IX_Users_IsSeller", table: "Users", column: "IsSeller");

            // Anyone who already listed a car is a seller; taking the panel away from them
            // would be a regression they did nothing to deserve.
            migrationBuilder.Sql(@"
                UPDATE ""Users"" u
                   SET ""IsSeller"" = TRUE,
                       ""SellerSinceUtc"" = COALESCE(u.""RegisteredUtc"", NOW())
                 WHERE EXISTS (SELECT 1 FROM ""Cars"" c WHERE c.""OwnerId"" = u.""Id"");
            ");

            // ---------- Cars: vehicle specification ----------

            migrationBuilder.RenameColumn(name: "StartingPrice", table: "Cars", newName: "Price");

            migrationBuilder.AddColumn<int>(
                name: "Mileage", table: "Cars", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ServiceHistory", table: "Cars", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ServiceHistoryNotes", table: "Cars", type: "character varying(2000)",
                maxLength: 2000, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FuelType", table: "Cars", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Transmission", table: "Cars", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Drivetrain", table: "Cars", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EngineSizeCc", table: "Cars", type: "integer", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PowerHp", table: "Cars", type: "integer", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Doors", table: "Cars", type: "integer", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Seats", table: "Cars", type: "integer", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExteriorColour", table: "Cars", type: "character varying(40)",
                maxLength: 40, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousOwners", table: "Cars", type: "integer", nullable: true);

            // 1 == VehicleCondition.Used, the right assumption for existing stock.
            migrationBuilder.AddColumn<int>(
                name: "Condition", table: "Cars", type: "integer", nullable: false, defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "HasAccidentHistory", table: "Cars", type: "boolean",
                nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Vin", table: "Cars", type: "character varying(17)", maxLength: 17, nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstRegistration", table: "Cars", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City", table: "Cars", type: "character varying(80)", maxLength: 80, nullable: true);

            // ---------- Cars: lifecycle ----------

            migrationBuilder.AddColumn<int>(
                name: "Status", table: "Cars", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedUtc", table: "Cars", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SoldUtc", table: "Cars", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SoldPrice", table: "Cars", type: "numeric(18,2)",
                precision: 18, scale: 2, nullable: true);

            // Status replaces IsPublished + IsSold, so it has to be derived from them before
            // they are dropped. 3 == Sold, 1 == Active, 0 == Draft.
            migrationBuilder.Sql(@"
                UPDATE ""Cars""
                   SET ""Status"" = CASE
                                      WHEN ""IsSold"" THEN 3
                                      WHEN ""IsPublished"" THEN 1
                                      ELSE 0
                                    END,
                       ""PublishedUtc"" = CASE WHEN ""IsPublished"" THEN ""CreatedUtc"" ELSE NULL END;
            ");

            // The old auction recorded a winner but not what they paid. Recover the sale price
            // from the winning bid, which was always the highest one.
            migrationBuilder.Sql(@"
                UPDATE ""Cars"" c
                   SET ""SoldPrice"" = sub.""Amount"",
                       ""SoldUtc""   = COALESCE(c.""AuctionEnd"", sub.""CreatedUtc"")
                  FROM (
                        SELECT DISTINCT ON (b.""CarId"") b.""CarId"", b.""Amount"", b.""CreatedUtc""
                          FROM ""Bids"" b
                         ORDER BY b.""CarId"", b.""Amount"" DESC, b.""Id"" DESC
                       ) AS sub
                 WHERE sub.""CarId"" = c.""Id"" AND c.""IsSold"";
            ");

            // ---------- Bids become Offers ----------
            // RENAME, never drop and recreate: these rows are the offer history.

            migrationBuilder.Sql(@"ALTER TABLE ""Bids"" RENAME TO ""Offers"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME COLUMN ""BidderId"" TO ""BuyerId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME COLUMN ""BidderName"" TO ""BuyerUsername"";");

            // Postgres carries constraints and indexes through a table rename under their old
            // names; renaming them too keeps the schema matching what EF expects next time.
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME CONSTRAINT ""PK_Bids"" TO ""PK_Offers"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME CONSTRAINT ""FK_Bids_Cars_CarId"" TO ""FK_Offers_Cars_CarId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME CONSTRAINT ""FK_Bids_Users_BidderId"" TO ""FK_Offers_Users_BuyerId"";");
            migrationBuilder.Sql(@"ALTER INDEX ""IX_Bids_CarId"" RENAME TO ""IX_Offers_CarId"";");
            migrationBuilder.Sql(@"ALTER INDEX ""IX_Bids_BidderId"" RENAME TO ""IX_Offers_BuyerId"";");

            migrationBuilder.AddColumn<string>(
                name: "Message", table: "Offers", type: "character varying(1000)",
                maxLength: 1000, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status", table: "Offers", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedUtc", table: "Offers", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerResponse", table: "Offers", type: "character varying(1000)",
                maxLength: 1000, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConversationId", table: "Offers", type: "integer", nullable: true);

            // Reconstruct what the auction had already decided: on a car that sold, the
            // winning bid is Accepted (1) and the rest are Declined (2). Offers on cars that
            // never sold stay Pending (0) and land in their seller's new inbox.
            migrationBuilder.Sql(@"
                UPDATE ""Offers"" o
                   SET ""Status"" = 2,
                       ""RespondedUtc"" = c.""SoldUtc"",
                       ""SellerResponse"" = 'This listing closed under the previous auction system.'
                  FROM ""Cars"" c
                 WHERE c.""Id"" = o.""CarId"" AND c.""IsSold"";
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Offers"" o
                   SET ""Status"" = 1,
                       ""SellerResponse"" = NULL
                  FROM ""Cars"" c
                 WHERE c.""Id"" = o.""CarId""
                   AND c.""IsSold""
                   AND o.""Id"" = (
                        SELECT b.""Id"" FROM ""Offers"" b
                         WHERE b.""CarId"" = c.""Id""
                         ORDER BY b.""Amount"" DESC, b.""Id"" DESC
                         LIMIT 1);
            ");

            // ---------- Retire the auction columns ----------

            migrationBuilder.DropIndex(name: "IX_Cars_IsPublished", table: "Cars");
            migrationBuilder.DropColumn(name: "AuctionEnd", table: "Cars");
            migrationBuilder.DropColumn(name: "ClosureProcessed", table: "Cars");
            migrationBuilder.DropColumn(name: "IsPublished", table: "Cars");
            migrationBuilder.DropColumn(name: "IsSold", table: "Cars");

            migrationBuilder.CreateIndex(name: "IX_Cars_Status", table: "Cars", column: "Status");
            migrationBuilder.CreateIndex(
                name: "IX_Cars_Make_Model", table: "Cars", columns: new[] { "Make", "Model" });

            // ---------- Messaging ----------

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    OfferId = table.Column<int>(type: "integer", nullable: true),
                    BuyerId = table.Column<int>(type: "integer", nullable: false),
                    SellerId = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_Cars_CarId",
                        column: x => x.CarId,
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Conversations_Users_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Conversations_Users_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    SenderId = table.Column<int>(type: "integer", nullable: true),
                    SenderUsername = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Offers point at their thread. Added after Conversations exists, and kept
            // SetNull so deleting a thread never destroys the record of the offer itself.
            migrationBuilder.CreateIndex(
                name: "IX_Offers_ConversationId", table: "Offers", column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_CarId_Status", table: "Offers", columns: new[] { "CarId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Offers_Conversations_ConversationId",
                table: "Offers",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BuyerId", table: "Conversations", column: "BuyerId");

            // One thread per buyer per listing: a second offer continues the existing chat.
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CarId_BuyerId", table: "Conversations",
                columns: new[] { "CarId", "BuyerId" }, unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_LastMessageUtc", table: "Conversations", column: "LastMessageUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_SellerId", table: "Conversations", column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SentUtc", table: "Messages",
                columns: new[] { "ConversationId", "SentUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId", table: "Messages", column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Offers_Conversations_ConversationId", table: "Offers");
            migrationBuilder.DropIndex(name: "IX_Offers_ConversationId", table: "Offers");
            migrationBuilder.DropIndex(name: "IX_Offers_CarId_Status", table: "Offers");

            migrationBuilder.DropTable(name: "Conversations");

            // Put the auction columns back before deriving their values from Status.
            migrationBuilder.AddColumn<DateTime>(
                name: "AuctionEnd", table: "Cars", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "ClosureProcessed", table: "Cars", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished", table: "Cars", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "IsSold", table: "Cars", type: "boolean", nullable: false, defaultValue: false);

            migrationBuilder.Sql(@"
                UPDATE ""Cars""
                   SET ""IsSold"" = (""Status"" = 3),
                       ""IsPublished"" = (""Status"" IN (1, 2, 3)),
                       ""ClosureProcessed"" = (""Status"" = 3),
                       ""AuctionEnd"" = ""SoldUtc"";
            ");

            migrationBuilder.DropIndex(name: "IX_Cars_Status", table: "Cars");
            migrationBuilder.DropIndex(name: "IX_Cars_Make_Model", table: "Cars");
            migrationBuilder.CreateIndex(name: "IX_Cars_IsPublished", table: "Cars", column: "IsPublished");

            foreach (var column in new[]
                     {
                         "Mileage", "ServiceHistory", "ServiceHistoryNotes", "FuelType", "Transmission",
                         "Drivetrain", "EngineSizeCc", "PowerHp", "Doors", "Seats", "ExteriorColour",
                         "PreviousOwners", "Condition", "HasAccidentHistory", "Vin", "FirstRegistration",
                         "City", "Status", "PublishedUtc", "SoldUtc", "SoldPrice"
                     })
            {
                migrationBuilder.DropColumn(name: column, table: "Cars");
            }

            migrationBuilder.RenameColumn(name: "Price", table: "Cars", newName: "StartingPrice");

            // Offers go back to being Bids, again by rename so no row is lost.
            foreach (var column in new[] { "Message", "Status", "RespondedUtc", "SellerResponse", "ConversationId" })
            {
                migrationBuilder.DropColumn(name: column, table: "Offers");
            }

            migrationBuilder.Sql(@"ALTER INDEX ""IX_Offers_BuyerId"" RENAME TO ""IX_Bids_BidderId"";");
            migrationBuilder.Sql(@"ALTER INDEX ""IX_Offers_CarId"" RENAME TO ""IX_Bids_CarId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME CONSTRAINT ""FK_Offers_Users_BuyerId"" TO ""FK_Bids_Users_BidderId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME CONSTRAINT ""FK_Offers_Cars_CarId"" TO ""FK_Bids_Cars_CarId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME CONSTRAINT ""PK_Offers"" TO ""PK_Bids"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME COLUMN ""BuyerUsername"" TO ""BidderName"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME COLUMN ""BuyerId"" TO ""BidderId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Offers"" RENAME TO ""Bids"";");

            migrationBuilder.DropIndex(name: "IX_Users_IsSeller", table: "Users");

            foreach (var column in new[]
                     {
                         "IsSeller", "SellerSinceUtc", "SellerType",
                         "SellerDisplayName", "SellerLocation", "SellerVerified"
                     })
            {
                migrationBuilder.DropColumn(name: column, table: "Users");
            }
        }
    }
}
