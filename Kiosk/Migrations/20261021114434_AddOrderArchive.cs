using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_archive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SeqNo = table.Column<int>(type: "int", nullable: false),
                    OrderMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TableNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ServiceChargePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ServiceChargeAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    GstPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BillType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SplitCount = table.Column<int>(type: "int", nullable: false),
                    SplitAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WaiterId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    MergedIntoOrderId = table.Column<int>(type: "int", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_archive", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_archive_config_terminal_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "config_terminal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_archive_users_WaiterId",
                        column: x => x.WaiterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_archive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    MenuItemExternalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsFOC = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_archive", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_item_archive_order_archive_OrderId",
                        column: x => x.OrderId,
                        principalTable: "order_archive",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_payment_archive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Tendered = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ChangeGiven = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ReceivedBy = table.Column<int>(type: "int", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_payment_archive", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_payment_archive_order_archive_OrderId",
                        column: x => x.OrderId,
                        principalTable: "order_archive",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_archive_BusinessDate_Status",
                table: "order_archive",
                columns: new[] { "BusinessDate", "Status" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_order_archive_BusinessDate_WaiterId",
                table: "order_archive",
                columns: new[] { "BusinessDate", "WaiterId" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_order_archive_MergedIntoOrderId",
                table: "order_archive",
                column: "MergedIntoOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_archive_TerminalId",
                table: "order_archive",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_order_archive_WaiterId",
                table: "order_archive",
                column: "WaiterId");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_archive_OrderId",
                table: "order_item_archive",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_payment_archive_OrderId",
                table: "order_payment_archive",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_payment_archive_PaidAt_Method",
                table: "order_payment_archive",
                columns: new[] { "PaidAt", "Method" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_archive");

            migrationBuilder.DropTable(
                name: "order_payment_archive");

            migrationBuilder.DropTable(
                name: "order_archive");
        }
    }
}
