using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSubCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MenuItems_Categories_SubCategoryExternalId",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_SubCategoryExternalId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "SubCategoryExternalId",
                table: "MenuItems");

            // Sub categories were level 2 rows in the same table. Their items keep their
            // main category, so the rows themselves can go - left in place they would
            // turn up as tiles on the till's rail once Level is gone.
            migrationBuilder.Sql("DELETE FROM Categories WHERE Level = 2");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ParentExternalId",
                table: "Categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubCategoryExternalId",
                table: "MenuItems",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Categories",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ParentExternalId",
                table: "Categories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_SubCategoryExternalId",
                table: "MenuItems",
                column: "SubCategoryExternalId");

            migrationBuilder.AddForeignKey(
                name: "FK_MenuItems_Categories_SubCategoryExternalId",
                table: "MenuItems",
                column: "SubCategoryExternalId",
                principalTable: "Categories",
                principalColumn: "ExternalId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
