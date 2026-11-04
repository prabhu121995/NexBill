using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class RemoveHeldOrderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Holds already recalled or discarded were kept only as history. Remove
            // them before Status goes, or they would show up again as live holds.
            // Their items go with them by the cascade on held_order_item.
            migrationBuilder.Sql("DELETE FROM held_order WHERE Status <> 'held';");

            migrationBuilder.DropIndex(
                name: "IX_held_order_Status_HeldAt",
                table: "held_order");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "held_order");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "held_order");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "held_order");

            migrationBuilder.CreateIndex(
                name: "IX_held_order_HeldAt",
                table: "held_order",
                column: "HeldAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_held_order_HeldAt",
                table: "held_order");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "held_order",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClosedBy",
                table: "held_order",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "held_order",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "held");

            migrationBuilder.CreateIndex(
                name: "IX_held_order_Status_HeldAt",
                table: "held_order",
                columns: new[] { "Status", "HeldAt" });
        }
    }
}
