using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistTrackOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old join table (PlayListsId, TracksId) carries real data and has no ordering
            // column, so it's rebuilt from scratch rather than renamed in place - a plain rename
            // would silently corrupt existing playlist/track associations. Dropping the old table
            // below removes its foreign keys implicitly; SQLite's provider doesn't support
            // DropForeignKeyOperation as a standalone step.
            migrationBuilder.RenameTable(
                name: "PlayListTrack",
                newName: "PlayListTrack_Old");

            migrationBuilder.CreateTable(
                name: "PlayListTrack",
                columns: table => new
                {
                    PlaylistId = table.Column<uint>(type: "INTEGER", nullable: false),
                    TrackId = table.Column<uint>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayListTrack", x => new { x.PlaylistId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_PlayListTrack_PlayLists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "PlayLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayListTrack_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve existing playlist/track associations, deriving an initial Position
            // per playlist from the old table's insertion order (rowid).
            migrationBuilder.Sql(
                """
                INSERT INTO "PlayListTrack" ("PlaylistId", "TrackId", "Position")
                SELECT "PlayListsId", "TracksId",
                       ROW_NUMBER() OVER (PARTITION BY "PlayListsId" ORDER BY rowid) - 1
                FROM "PlayListTrack_Old";
                """);

            migrationBuilder.DropTable(
                name: "PlayListTrack_Old");

            migrationBuilder.CreateIndex(
                name: "IX_PlayListTrack_PlaylistId_Position",
                table: "PlayListTrack",
                columns: new[] { "PlaylistId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayListTrack_TrackId",
                table: "PlayListTrack",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "PlayListTrack",
                newName: "PlayListTrack_Old");

            migrationBuilder.CreateTable(
                name: "PlayListTrack",
                columns: table => new
                {
                    PlayListsId = table.Column<uint>(type: "INTEGER", nullable: false),
                    TracksId = table.Column<uint>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayListTrack", x => new { x.PlayListsId, x.TracksId });
                    table.ForeignKey(
                        name: "FK_PlayListTrack_PlayLists_PlayListsId",
                        column: x => x.PlayListsId,
                        principalTable: "PlayLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayListTrack_Tracks_TracksId",
                        column: x => x.TracksId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Ordering has no representation in the old schema, so Position is dropped here.
            migrationBuilder.Sql(
                """
                INSERT INTO "PlayListTrack" ("PlayListsId", "TracksId")
                SELECT "PlaylistId", "TrackId"
                FROM "PlayListTrack_Old";
                """);

            migrationBuilder.DropTable(
                name: "PlayListTrack_Old");

            migrationBuilder.CreateIndex(
                name: "IX_PlayListTrack_TracksId",
                table: "PlayListTrack",
                column: "TracksId");
        }
    }
}
