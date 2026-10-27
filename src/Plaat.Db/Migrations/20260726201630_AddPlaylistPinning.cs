using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "SmartPlayLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "PlayLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "PlayLists");
        }
    }
}
