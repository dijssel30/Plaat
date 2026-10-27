using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class ConvertQueueItemLinksTrackLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConvertQueueItems_Tracks_TrackId",
                table: "ConvertQueueItems");

            // TrackId currently holds a Track.Id. Rewrite it in place to hold the id of that
            // track's first (i.e. originally-scanned, unsupported-format) location instead -
            // mirroring the old runtime behavior of Track.TrackLocations.First() - before the
            // column below is renamed and repointed to TrackLocations, so any item still queued
            // for conversion keeps referencing the right file.
            migrationBuilder.Sql(
                """
                UPDATE ConvertQueueItems
                SET TrackId = (
                    SELECT tl.Id
                    FROM TrackLocations tl
                    WHERE tl.TrackId = ConvertQueueItems.TrackId
                    ORDER BY tl.Id
                    LIMIT 1
                );
                """);

            migrationBuilder.RenameColumn(
                name: "TrackId",
                table: "ConvertQueueItems",
                newName: "TrackLocationId");

            migrationBuilder.RenameIndex(
                name: "IX_ConvertQueueItems_TrackId",
                table: "ConvertQueueItems",
                newName: "IX_ConvertQueueItems_TrackLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConvertQueueItems_TrackLocations_TrackLocationId",
                table: "ConvertQueueItems",
                column: "TrackLocationId",
                principalTable: "TrackLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConvertQueueItems_TrackLocations_TrackLocationId",
                table: "ConvertQueueItems");

            migrationBuilder.RenameColumn(
                name: "TrackLocationId",
                table: "ConvertQueueItems",
                newName: "TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_ConvertQueueItems_TrackLocationId",
                table: "ConvertQueueItems",
                newName: "IX_ConvertQueueItems_TrackId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConvertQueueItems_Tracks_TrackId",
                table: "ConvertQueueItems",
                column: "TrackId",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
