using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackTrimPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "TrimEnd",
                table: "Tracks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TrimStart",
                table: "Tracks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrimEnd",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "TrimStart",
                table: "Tracks");
        }
    }
}
