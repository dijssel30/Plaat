using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoredFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoredFolders",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    LastScannedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredFolders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredFolders_Path",
                table: "MonitoredFolders",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoredFolders");
        }
    }
}
