using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddLoudnessNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LoudnessNormalizationEnabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<float>(
                name: "IntegratedLoudnessLufs",
                table: "Tracks",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoudnessNormalizationEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegratedLoudnessLufs",
                table: "Tracks");
        }
    }
}
