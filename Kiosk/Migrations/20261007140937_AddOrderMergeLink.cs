using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderMergeLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "MergedIntoOrderId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessDate_Status",
                table: "Orders",
                columns: new[] { "BusinessDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_MergedIntoOrderId",
                table: "Orders",
                column: "MergedIntoOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Orders_MergedIntoOrderId",
                table: "Orders",
                column: "MergedIntoOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Orders_MergedIntoOrderId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_BusinessDate_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_MergedIntoOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MergedIntoOrderId",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
