using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <inheritdoc />
    public partial class PromotionReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: true),
                    CarTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SellerUserId = table.Column<int>(type: "integer", nullable: true),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedEarlyUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PriceEur = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotions_Cars_CarId",
                        column: x => x.CarId,
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_CarId_StartedUtc",
                table: "Promotions",
                columns: new[] { "CarId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Reference",
                table: "Promotions",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_StartedUtc",
                table: "Promotions",
                column: "StartedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Promotions");
        }
    }
}
