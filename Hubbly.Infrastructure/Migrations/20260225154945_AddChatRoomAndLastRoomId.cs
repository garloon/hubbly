using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hubbly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRoomAndLastRoomId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastRoomId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRooms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_LastRoomId",
                table: "Users",
                column: "LastRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_IsActive",
                table: "ChatRooms",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_IsActive_Type",
                table: "ChatRooms",
                columns: new[] { "IsActive", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_LastActiveAt",
                table: "ChatRooms",
                column: "LastActiveAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_Type",
                table: "ChatRooms",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatRooms");

            migrationBuilder.DropIndex(
                name: "IX_Users_LastRoomId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastRoomId",
                table: "Users");
        }
    }
}
