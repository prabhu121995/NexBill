using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class AddHeldOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "held_order",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "held"),
                    OrderMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TableNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    WaiterId = table.Column<int>(type: "int", nullable: true),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    HeldBy = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    HeldAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedBy = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_held_order", x => x.Id);
                    table.ForeignKey(
                        name: "FK_held_order_config_terminal_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "config_terminal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_held_order_users_WaiterId",
                        column: x => x.WaiterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "held_order_item",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeldOrderId = table.Column<int>(type: "int", nullable: false),
                    MenuItemExternalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsFOC = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_held_order_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_held_order_item_held_order_HeldOrderId",
                        column: x => x.HeldOrderId,
                        principalTable: "held_order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_held_order_Status_HeldAt",
                table: "held_order",
                columns: new[] { "Status", "HeldAt" });

            migrationBuilder.CreateIndex(
                name: "IX_held_order_TerminalId",
                table: "held_order",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_held_order_WaiterId",
                table: "held_order",
                column: "WaiterId");

            migrationBuilder.CreateIndex(
                name: "IX_held_order_item_HeldOrderId",
                table: "held_order_item",
                column: "HeldOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "held_order_item");

            migrationBuilder.DropTable(
                name: "held_order");
        }
    }
}
