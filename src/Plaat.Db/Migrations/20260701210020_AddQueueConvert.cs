using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueConvert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalContentHash",
                table: "Tracks",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConvertQueueItems",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvertQueueItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConvertQueueItems_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConvertQueueItems_TrackId",
                table: "ConvertQueueItems",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConvertQueueItems");

            migrationBuilder.DropColumn(
                name: "OriginalContentHash",
                table: "Tracks");
        }
    }
}
