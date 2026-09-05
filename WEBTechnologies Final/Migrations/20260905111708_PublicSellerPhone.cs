using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WEBTechnologies_Final.Migrations
{
    /// <inheritdoc />
    public partial class PublicSellerPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicPhone",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicPhone",
                table: "Users");
        }
    }
}
