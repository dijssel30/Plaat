using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing tokens were stored in plaintext and are being replaced by hashes;
            // there's no way to recover a hash from what's stored, so these rows are dead
            // regardless. Clear them out rather than giving them a colliding "" TokenHash,
            // which would violate the new unique index below. Affected sessions simply need
            // to log in again to get a fresh refresh token.
            migrationBuilder.Sql("DELETE FROM \"RefreshTokens\";");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");
        }
    }
}
