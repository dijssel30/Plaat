using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddUserToPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "SmartPlayLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "PlayLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlayLists_UserId",
                table: "SmartPlayLists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayLists_UserId",
                table: "PlayLists",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayLists_Users_UserId",
                table: "PlayLists",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SmartPlayLists_Users_UserId",
                table: "SmartPlayLists",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayLists_Users_UserId",
                table: "PlayLists");

            migrationBuilder.DropForeignKey(
                name: "FK_SmartPlayLists_Users_UserId",
                table: "SmartPlayLists");

            migrationBuilder.DropIndex(
                name: "IX_SmartPlayLists_UserId",
                table: "SmartPlayLists");

            migrationBuilder.DropIndex(
                name: "IX_PlayLists_UserId",
                table: "PlayLists");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PlayLists");
        }
    }
}
