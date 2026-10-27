using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class SmartPlaylistRuleGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SmartPlayListRule_SmartPlayLists_PlaylistId",
                table: "SmartPlayListRule");

            migrationBuilder.RenameColumn(
                name: "PlaylistId",
                table: "SmartPlayListRule",
                newName: "GroupId");

            migrationBuilder.RenameIndex(
                name: "IX_SmartPlayListRule_PlaylistId",
                table: "SmartPlayListRule",
                newName: "IX_SmartPlayListRule_GroupId");

            migrationBuilder.AddColumn<int>(
                name: "Limit",
                table: "SmartPlayLists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LimitOrderBy",
                table: "SmartPlayLists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "RootGroupId",
                table: "SmartPlayLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "TrackCount",
                table: "SmartPlayLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrackCountUpdatedAt",
                table: "SmartPlayLists",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AddedDateRule_Value",
                table: "SmartPlayListRule",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "AddedWithinDaysPlaylistRule_Days",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Bitrate",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BitrateRuleMatchType",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DateRuleMatchType",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "Days",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MaxDuration",
                table: "SmartPlayListRule",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MinDuration",
                table: "SmartPlayListRule",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "Year",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearRuleMatchType",
                table: "SmartPlayListRule",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmartPlaylistRuleGroup",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Combinator = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentGroupId = table.Column<uint>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartPlaylistRuleGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartPlaylistRuleGroup_SmartPlaylistRuleGroup_ParentGroupId",
                        column: x => x.ParentGroupId,
                        principalTable: "SmartPlaylistRuleGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlayLists_RootGroupId",
                table: "SmartPlayLists",
                column: "RootGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlaylistRuleGroup_ParentGroupId",
                table: "SmartPlaylistRuleGroup",
                column: "ParentGroupId");

            // Data backfill: every pre-existing SmartPlayLists row had its rules directly attached
            // (implicitly AND-combined); give each one a new root AND-group and move its rules under
            // it. At this point in the migration the new FK constraints below haven't been added yet,
            // so this can freely insert/update without violating referential integrity mid-flight.

            // One new root group (Combinator = 0 / And, no parent) per existing smart playlist, in
            // SmartPlayLists' natural row order (safe here: an unfiltered, unmodified SELECT).
            migrationBuilder.Sql(
                "INSERT INTO SmartPlaylistRuleGroup (Combinator, ParentGroupId) " +
                "SELECT 0, NULL FROM SmartPlayLists;");

            // Link each playlist to its new root group. Correlated via ROW_NUMBER() rather than
            // rowid, since rowid correlation across two different tables isn't guaranteed stable.
            migrationBuilder.Sql(@"
                WITH ordered_playlists AS (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM SmartPlayLists
                ), ordered_groups AS (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM SmartPlaylistRuleGroup
                )
                UPDATE SmartPlayLists
                SET RootGroupId = (
                    SELECT g.Id FROM ordered_groups g
                    JOIN ordered_playlists p ON p.rn = g.rn
                    WHERE p.Id = SmartPlayLists.Id
                );
            ");

            // SmartPlayListRule.GroupId currently still holds the *old* PlaylistId values (renamed
            // column, values untouched) — repoint each rule at its playlist's new root group.
            migrationBuilder.Sql(@"
                UPDATE SmartPlayListRule
                SET GroupId = (
                    SELECT RootGroupId FROM SmartPlayLists WHERE SmartPlayLists.Id = SmartPlayListRule.GroupId
                );
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_SmartPlayListRule_SmartPlaylistRuleGroup_GroupId",
                table: "SmartPlayListRule",
                column: "GroupId",
                principalTable: "SmartPlaylistRuleGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SmartPlayLists_SmartPlaylistRuleGroup_RootGroupId",
                table: "SmartPlayLists",
                column: "RootGroupId",
                principalTable: "SmartPlaylistRuleGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <summary>
        /// Best-effort reverse migration: only correctly restores <c>PlaylistId</c> for rules that
        /// are direct children of a playlist's (un-nested, AND-combined) root group — the only shape
        /// the old flat schema could represent. A playlist that was edited to use OR-combination or
        /// nested child groups has no equivalent in the old schema; rules living in a nested group
        /// have no matching row in the correlated update below, so the final <c>AddForeignKey</c>
        /// call at the end of this method will fail loudly for such data rather than silently
        /// restoring wrong <c>PlaylistId</c> values.
        /// </summary>
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE SmartPlayListRule
                SET GroupId = (
                    SELECT sp.Id FROM SmartPlayLists sp WHERE sp.RootGroupId = SmartPlayListRule.GroupId
                )
                WHERE GroupId IN (SELECT RootGroupId FROM SmartPlayLists);
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_SmartPlayListRule_SmartPlaylistRuleGroup_GroupId",
                table: "SmartPlayListRule");

            migrationBuilder.DropForeignKey(
                name: "FK_SmartPlayLists_SmartPlaylistRuleGroup_RootGroupId",
                table: "SmartPlayLists");

            migrationBuilder.DropTable(
                name: "SmartPlaylistRuleGroup");

            migrationBuilder.DropIndex(
                name: "IX_SmartPlayLists_RootGroupId",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "Limit",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "LimitOrderBy",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "RootGroupId",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "TrackCount",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "TrackCountUpdatedAt",
                table: "SmartPlayLists");

            migrationBuilder.DropColumn(
                name: "AddedDateRule_Value",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "AddedWithinDaysPlaylistRule_Days",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "Bitrate",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "BitrateRuleMatchType",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "DateRuleMatchType",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "Days",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "MaxDuration",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "MinDuration",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "SmartPlayListRule");

            migrationBuilder.DropColumn(
                name: "YearRuleMatchType",
                table: "SmartPlayListRule");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "SmartPlayListRule",
                newName: "PlaylistId");

            migrationBuilder.RenameIndex(
                name: "IX_SmartPlayListRule_GroupId",
                table: "SmartPlayListRule",
                newName: "IX_SmartPlayListRule_PlaylistId");

            migrationBuilder.AddForeignKey(
                name: "FK_SmartPlayListRule_SmartPlayLists_PlaylistId",
                table: "SmartPlayListRule",
                column: "PlaylistId",
                principalTable: "SmartPlayLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
