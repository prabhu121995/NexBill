using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class StoreCurrencyAndPayNow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencySymbol",
                table: "config_store",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PayNowEnabled",
                table: "config_store",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PayNowMerchantName",
                table: "config_store",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PayNowProxy",
                table: "config_store",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PayNowType",
                table: "config_store",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "uen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencySymbol",
                table: "config_store");

            migrationBuilder.DropColumn(
                name: "PayNowEnabled",
                table: "config_store");

            migrationBuilder.DropColumn(
                name: "PayNowMerchantName",
                table: "config_store");

            migrationBuilder.DropColumn(
                name: "PayNowProxy",
                table: "config_store");

            migrationBuilder.DropColumn(
                name: "PayNowType",
                table: "config_store");
        }
    }
}
