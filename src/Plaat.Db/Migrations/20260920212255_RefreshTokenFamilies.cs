using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "FamilyId",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByTokenHash",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: true);

            // Every pre-existing token becomes a family of one. Sharing a family would mean that
            // revoking one could take out an unrelated session that happened to predate this
            // migration. ReplacedByTokenHash stays null for all of them, so none can be replayed
            // through the rotation grace period either - a token from before this migration is
            // rotated exactly as strictly as it was before.
            // CreatedAt is recovered from ExpiresAt, which has always been creation + 30 days;
            // substr trims the fractional seconds EF writes so SQLite parses it reliably.
            migrationBuilder.Sql(
                """
                UPDATE "RefreshTokens"
                SET "FamilyId" = 'legacy-' || "Id",
                    "CreatedAt" = datetime(substr("ExpiresAt", 1, 19), '-30 days')
                WHERE "FamilyId" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ReplacedByTokenHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "RefreshTokens");
        }
    }
}
