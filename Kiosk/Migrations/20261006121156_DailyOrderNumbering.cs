using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class DailyOrderNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "Orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Existing rows predate the column. CreatedAt is UTC, so convert it to
            // the cafe's local date — otherwise orders placed before 8am Singapore
            // time would be filed under the previous day.
            migrationBuilder.Sql("""
                UPDATE Orders
                SET BusinessDate = CAST(
                    CreatedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Singapore Standard Time' AS date);
                """);

            // SeqNo was previously a single all-time counter. Renumber it per day so
            // history matches the new scheme and the unique index below can be created.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY BusinessDate ORDER BY CreatedAt, Id) AS rn
                    FROM Orders
                )
                UPDATE o SET o.SeqNo = n.rn
                FROM Orders o
                INNER JOIN numbered n ON n.Id = o.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessDate_SeqNo",
                table: "Orders",
                columns: new[] { "BusinessDate", "SeqNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_BusinessDate_SeqNo",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "Orders");
        }
    }
}
