using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAdminAndPlayUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Plays",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plays_UserId",
                table: "Plays",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Plays_Users_UserId",
                table: "Plays",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Plays_Users_UserId",
                table: "Plays");

            migrationBuilder.DropIndex(
                name: "IX_Plays_UserId",
                table: "Plays");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Plays");
        }
    }
}
