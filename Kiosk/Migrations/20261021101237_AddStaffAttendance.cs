using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FirstInAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastInAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastOutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    InBy = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OutBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_attendance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_staff_attendance_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staff_attendance_BusinessDate",
                table: "staff_attendance",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_staff_attendance_UserId_BusinessDate",
                table: "staff_attendance",
                columns: new[] { "UserId", "BusinessDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_attendance");
        }
    }
}
