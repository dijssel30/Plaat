using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayIsImported : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsImported",
                table: "Plays",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Existing imports with weight 1 are indistinguishable from real plays, so they are ignored.
            migrationBuilder.Sql("UPDATE \"Plays\" SET \"IsImported\" = 1 WHERE \"Weight\" > 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsImported",
                table: "Plays");
        }
    }
}
