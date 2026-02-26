using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hubbly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIsActiveAndAddCurrentUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add CurrentUsers column
            migrationBuilder.AddColumn<int>(
                name: "CurrentUsers",
                table: "ChatRooms",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Drop indexes on IsActive
            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_IsActive",
                table: "ChatRooms");

            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_IsActive_Type",
                table: "ChatRooms");

            // Drop IsActive column
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ChatRooms");

            // Initialize CurrentUsers for existing rooms
            migrationBuilder.Sql("UPDATE \"ChatRooms\" SET \"CurrentUsers\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add IsActive column
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ChatRooms",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Recreate indexes on IsActive
            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_IsActive",
                table: "ChatRooms",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_IsActive_Type",
                table: "ChatRooms",
                columns: new[] { "IsActive", "Type" });

            // Drop CurrentUsers column
            migrationBuilder.DropColumn(
                name: "CurrentUsers",
                table: "ChatRooms");
        }
    }
}
