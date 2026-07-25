using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToPayPalProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StripeSessionId",
                table: "Payments",
                newName: "ProviderOrderId");

            migrationBuilder.RenameColumn(
                name: "StripePaymentIntentId",
                table: "Payments",
                newName: "ProviderCaptureId");

            migrationBuilder.RenameIndex(
                name: "IX_Payments_StripeSessionId",
                table: "Payments",
                newName: "IX_Payments_ProviderOrderId");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Payments",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Payments");

            migrationBuilder.RenameColumn(
                name: "ProviderOrderId",
                table: "Payments",
                newName: "StripeSessionId");

            migrationBuilder.RenameColumn(
                name: "ProviderCaptureId",
                table: "Payments",
                newName: "StripePaymentIntentId");

            migrationBuilder.RenameIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments",
                newName: "IX_Payments_StripeSessionId");
        }
    }
}
