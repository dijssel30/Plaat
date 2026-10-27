using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistDiscogsEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscogsArtistId",
                table: "Artists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscogsImagePath",
                table: "Artists",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscogsLookupStatus",
                table: "Artists",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotAttempted");

            migrationBuilder.AddColumn<string>(
                name: "DiscogsSummary",
                table: "Artists",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscogsArtistId",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "DiscogsImagePath",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "DiscogsLookupStatus",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "DiscogsSummary",
                table: "Artists");
        }
    }
}
