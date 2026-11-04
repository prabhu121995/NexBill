using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTerminalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TerminalId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TerminalId",
                table: "Orders",
                column: "TerminalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_config_terminal_TerminalId",
                table: "Orders",
                column: "TerminalId",
                principalTable: "config_terminal",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_config_terminal_TerminalId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TerminalId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TerminalId",
                table: "Orders");
        }
    }
}
