using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Stars",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StarsRuleMatchType",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackRating",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackId = table.Column<uint>(type: "INTEGER", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackRating", x => new { x.UserId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_TrackRating_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackRating_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackRating_TrackId",
                table: "TrackRating",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackRating");

            migrationBuilder.DropColumn(
                name: "Stars",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "StarsRuleMatchType",
                table: "SmartPlayListRule");
        }
    }
}
