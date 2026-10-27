using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plaat.Db.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoverArts",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverArts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayLists",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartPlayLists",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartPlayLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CoverArtId = table.Column<uint>(type: "INTEGER", nullable: true),
                    TotalTracks = table.Column<uint>(type: "INTEGER", nullable: false),
                    TotalDiscs = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_CoverArts_CoverArtId",
                        column: x => x.CoverArtId,
                        principalTable: "CoverArts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SmartPlayListRule",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<uint>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 21, nullable: false),
                    TextMatchType = table.Column<int>(type: "INTEGER", nullable: true),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    PlayCountPlaylistRuleType = table.Column<int>(type: "INTEGER", nullable: true),
                    PlayCount = table.Column<uint>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartPlayListRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartPlayListRule_SmartPlayLists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "SmartPlayLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlbumArtist",
                columns: table => new
                {
                    AlbumArtistsId = table.Column<uint>(type: "INTEGER", nullable: false),
                    AlbumsId = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumArtist", x => new { x.AlbumArtistsId, x.AlbumsId });
                    table.ForeignKey(
                        name: "FK_AlbumArtist_Albums_AlbumsId",
                        column: x => x.AlbumsId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlbumArtist_Artists_AlbumArtistsId",
                        column: x => x.AlbumArtistsId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlbumId = table.Column<uint>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TrackNumber = table.Column<uint>(type: "INTEGER", nullable: false),
                    DiscNumber = table.Column<uint>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    PlayCount = table.Column<uint>(type: "INTEGER", nullable: false),
                    Year = table.Column<uint>(type: "INTEGER", nullable: false),
                    PossiblyCorrupt = table.Column<bool>(type: "INTEGER", nullable: false),
                    BitRate = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleRate = table.Column<int>(type: "INTEGER", nullable: false),
                    BitsPerSample = table.Column<int>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tracks_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtistTrack",
                columns: table => new
                {
                    ArtistsId = table.Column<uint>(type: "INTEGER", nullable: false),
                    TracksId = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistTrack", x => new { x.ArtistsId, x.TracksId });
                    table.ForeignKey(
                        name: "FK_ArtistTrack_Artists_ArtistsId",
                        column: x => x.ArtistsId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArtistTrack_Tracks_TracksId",
                        column: x => x.TracksId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenreTrack",
                columns: table => new
                {
                    GenresId = table.Column<int>(type: "INTEGER", nullable: false),
                    TracksId = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenreTrack", x => new { x.GenresId, x.TracksId });
                    table.ForeignKey(
                        name: "FK_GenreTrack_Genres_GenresId",
                        column: x => x.GenresId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GenreTrack_Tracks_TracksId",
                        column: x => x.TracksId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayListTrack",
                columns: table => new
                {
                    PlayListsId = table.Column<uint>(type: "INTEGER", nullable: false),
                    TracksId = table.Column<uint>(type: "INTEGER", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "TrackLocations",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TrackId = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackLocations_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumArtist_AlbumsId",
                table: "AlbumArtist",
                column: "AlbumsId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_CoverArtId",
                table: "Albums",
                column: "CoverArtId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_Title",
                table: "Albums",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Name",
                table: "Artists",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistTrack_TracksId",
                table: "ArtistTrack",
                column: "TracksId");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Name",
                table: "Genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenreTrack_TracksId",
                table: "GenreTrack",
                column: "TracksId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayListTrack_TracksId",
                table: "PlayListTrack",
                column: "TracksId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlayListRule_PlaylistId",
                table: "SmartPlayListRule",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackLocations_Path",
                table: "TrackLocations",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackLocations_TrackId",
                table: "TrackLocations",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AlbumId",
                table: "Tracks",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_FileHash",
                table: "Tracks",
                column: "FileHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_Title",
                table: "Tracks",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumArtist");

            migrationBuilder.DropTable(
                name: "ArtistTrack");

            migrationBuilder.DropTable(
                name: "GenreTrack");

            migrationBuilder.DropTable(
                name: "PlayListTrack");

            migrationBuilder.DropTable(
                name: "SmartPlayListRule");

            migrationBuilder.DropTable(
                name: "TrackLocations");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "PlayLists");

            migrationBuilder.DropTable(
                name: "SmartPlayLists");

            migrationBuilder.DropTable(
                name: "Tracks");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "CoverArts");
        }
    }
}
