using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackLyrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lyrics",
                table: "Tracks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lyrics",
                table: "Tracks");
        }
    }
}
