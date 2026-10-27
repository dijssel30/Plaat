using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

/// <summary>
/// Endpoint behaviour not covered elsewhere: login/token/refresh, playlist editing and import,
/// admin scan/import triggers, favorites, cover and Discogs image serving, and user edge cases.
/// Each test uses its own IDs, since the repository substitutes are shared across the class.
/// </summary>
public sealed class ApiEndpointGapTests : IClassFixture<ApiGapFactory>
{
    private readonly ApiGapFactory _factory;
    private readonly HttpClient _client;

    public ApiEndpointGapTests(ApiGapFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private string TempFile(string name, string content = "")
    {
        var path = Path.Combine(_factory.WorkPath, $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, content);

        return path;
    }

    private static string Encode(string path)
    {
        return path.Replace("/", "%2F");
    }

    // ---- auth ----

    private void GivenPasswordHash(string username, string hash)
    {
        _factory.UserRepository.GetPasswordHash(username, Arg.Any<CancellationToken>()).Returns(hash);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsAccessTokenForThatUserAndARefreshToken()
    {
        GivenPasswordHash("alice", new PasswordHasher<string>().HashPassword("alice", ApiGapFactory.Password));
        _factory.UserRepository.CreateRefreshToken("alice", Arg.Any<CancellationToken>())
            .Returns(new RequestRefreshTokenResult("refresh-1", DateTime.UtcNow.AddDays(30)));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("alice", ApiGapFactory.Password));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokens!.RefreshToken.ShouldBe("refresh-1");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        jwt.Claims.ShouldContain(c => c.Value == "alice");
        jwt.Claims.ShouldNotContain(c => c.Value == "Admin");
    }

    [Fact]
    public async Task Login_AdminUser_GetsAdminRoleClaim()
    {
        GivenPasswordHash("testuser", new PasswordHasher<string>().HashPassword("testuser", ApiGapFactory.Password));
        _factory.UserRepository.CreateRefreshToken("testuser", Arg.Any<CancellationToken>())
            .Returns(new RequestRefreshTokenResult("refresh-admin", DateTime.UtcNow.AddDays(30)));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("testuser", ApiGapFactory.Password));

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        new JwtSecurityTokenHandler().ReadJwtToken(tokens!.AccessToken).Claims.ShouldContain(c => c.Value == "Admin");
    }

    [Fact]
    public async Task Login_WrongPassword_IsUnauthorized()
    {
        GivenPasswordHash("wrongpw", new PasswordHasher<string>().HashPassword("wrongpw", "the real one"));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wrongpw", "a guess"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownUser_IsUnauthorized()
    {
        _factory.UserRepository.GetPasswordHash("ghost", Arg.Any<CancellationToken>()).Returns((string?)null);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost", "whatever"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithLegacyHash_UpgradesTheStoredHash()
    {
        var legacyHasher = new PasswordHasher<string>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
        }));
        GivenPasswordHash("alice", legacyHasher.HashPassword("alice", ApiGapFactory.Password));
        _factory.UserRepository.CreateRefreshToken("alice", Arg.Any<CancellationToken>())
            .Returns(new RequestRefreshTokenResult("refresh-2", DateTime.UtcNow.AddDays(30)));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("alice", ApiGapFactory.Password));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.UserRepository.Received().UpdateHashedPassword("alice", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_FormLogin_ReturnsOAuthStyleBearerToken()
    {
        GivenPasswordHash("alice", new PasswordHasher<string>().HashPassword("alice", ApiGapFactory.Password));

        var response = await _client.PostAsync("/api/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = ApiGapFactory.Password
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>();
        body!.TokenType.ShouldBe("Bearer");
        body.ExpiresIn.ShouldBeGreaterThan(0);
        body.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("alice", " ")]
    [InlineData("alice", "wrong")]
    public async Task Token_MissingOrWrongCredentials_IsUnauthorized(string username, string password)
    {
        GivenPasswordHash("alice", new PasswordHasher<string>().HashPassword("alice", ApiGapFactory.Password));

        var response = await _client.PostAsync("/api/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsRotatedPair()
    {
        _factory.UserRepository.RotateRefreshToken("good-refresh", Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenRotationResult("alice", "next-refresh", DateTime.UtcNow.AddDays(30)));

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("good-refresh"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TokenResponse>())!.RefreshToken.ShouldBe("next-refresh");
    }

    [Fact]
    public async Task Refresh_RevokedOrUnknownToken_IsUnauthorized()
    {
        _factory.UserRepository.RotateRefreshToken("bad-refresh", Arg.Any<CancellationToken>())
            .Returns((RefreshTokenRotationResult?)null);

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("bad-refresh"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- playlists ----

    [Fact]
    public async Task GetAllPlaylists_ReturnsTheUsersPlaylists()
    {
        _factory.PlaylistRepository.GetAllPlaylists(1, Arg.Any<CancellationToken>())
            .Returns([new PlaylistOnlyDto { Id = 3, PlaylistName = "Mine", IsSmart = false, TrackCount = 2, IsPinned = true }]);

        var playlists = await _client.GetFromJsonAsync<PlaylistOnlyDto[]>("/api/playlists");

        playlists.ShouldHaveSingleItem().PlaylistName.ShouldBe("Mine");
    }

    [Fact]
    public async Task CreatePlaylist_DecodesSlashesInTheName()
    {
        var response = await _client.PostAsJsonAsync("/api/playlists/AC%2FDC mix", new uint[] { 1, 2 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.PlaylistRepository.Received(1).CreatePlaylist(
            "AC/DC mix", 1, Arg.Is<IReadOnlyCollection<uint>>(ids => ids!.SequenceEqual(new uint[] { 1, 2 })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlaylist_NameTaken_ReturnsConflict()
    {
        _factory.PlaylistRepository.CreatePlaylist("Taken", 1, Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new System.Data.DuplicateNameException()));

        var response = await _client.PostAsJsonAsync("/api/playlists/Taken", Array.Empty<uint>());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetPlaylist_Unknown_ReturnsNoContent()
    {
        var response = await _client.GetAsync("/api/playlists/nope%2Fnothing");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await _factory.PlaylistRepository.Received().GetPlaylist("nope/nothing", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSmartPlaylistRules_ReturnsRulesOrNoContent()
    {
        _factory.PlaylistRepository.GetSmartPlaylistRules(501, 1, Arg.Any<CancellationToken>())
            .Returns(new SmartPlaylistRulesDto { RootGroup = new SmartPlaylistRuleGroupDto(), Limit = 4 });

        (await _client.GetFromJsonAsync<SmartPlaylistRulesDto>("/api/playlists/smart/501/rules"))!.Limit.ShouldBe(4);
        (await _client.GetAsync("/api/playlists/smart/502/rules")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateSmartPlaylist_ReturnsOkOrNotFound()
    {
        _factory.PlaylistRepository.UpdateSmartPlaylist(
                511, 1, Arg.Any<SmartPlaylistRuleGroupDto>(), null, null, Arg.Any<CancellationToken>())
            .Returns(true);
        var request = new CreateSmartPlaylistRequest { RootGroup = new SmartPlaylistRuleGroupDto() };

        (await _client.PutAsJsonAsync("/api/playlists/smart/511", request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PutAsJsonAsync("/api/playlists/smart/512", request)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddRemoveDeleteAndPin_ForwardToTheRepository()
    {
        (await _client.PutAsJsonAsync("/api/playlists/521", new uint[] { 9 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.DeleteAsync("/api/playlists/521/tracks/9")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.DeleteAsync("/api/playlists/521?isSmart=true")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PutAsJsonAsync("/api/playlists/521/pin", new SetPlaylistPinnedRequest(false, true))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await _factory.PlaylistRepository.Received(1).AddToPlaylist(
            521, 1, Arg.Is<IReadOnlyCollection<uint>>(ids => ids!.Single() == 9), Arg.Any<CancellationToken>());
        await _factory.PlaylistRepository.Received(1).RemoveFromPlaylist(521, 1, 9, Arg.Any<CancellationToken>());
        await _factory.PlaylistRepository.Received(1).RemovePlaylist(521, true, 1, Arg.Any<CancellationToken>());
        await _factory.PlaylistRepository.Received(1).SetPlaylistPinned(521, false, 1, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReorderPlaylist_ReturnsOkOrBadRequest()
    {
        _factory.PlaylistRepository.ReorderPlaylist(531, 1, Arg.Any<IReadOnlyList<uint>>(), Arg.Any<CancellationToken>()).Returns(true);

        (await _client.PutAsJsonAsync("/api/playlists/531/order", new uint[] { 2, 1 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PutAsJsonAsync("/api/playlists/532/order", new uint[] { 2, 1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportPlaylist_ValidatesTheFileAndImportsM3u()
    {
        var m3u = TempFile("list.m3u", "#EXTM3U\n");
        var text = TempFile("list.txt");

        (await _client.PostAsync($"/api/playlists/import/{Encode(m3u)}", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsync($"/api/playlists/import/{Encode(text)}", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsync($"/api/playlists/import/{Encode("/nonexistent/x.m3u")}", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- admin scan / import triggers ----

    [Fact]
    public async Task ScanFolder_StartsAScanOfTheDecodedPath()
    {
        var folder = Path.Combine(_factory.WorkPath, "scan-me");

        var response = await _client.PostAsync($"/api/music/folder/{Encode(folder)}/scan", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<bool>()).ShouldBeTrue();
        await WaitFor(() => _factory.MusicScanner.ReceivedCalls().Any(c => c.GetArguments().Contains(folder)));
    }

    [Fact]
    public async Task ScanCsv_ValidatesTheFileAndStartsAnImport()
    {
        var csv = TempFile("tracks.csv");
        var notCsv = TempFile("tracks.txt");

        (await _client.PostAsync($"/api/music/file/{Encode(notCsv)}/scan", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsync($"/api/music/file/{Encode("/nonexistent/x.csv")}/scan", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsync($"/api/music/file/{Encode(csv)}/scan", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitFor(() => _factory.MusicScanner.ReceivedCalls().Any(c => c.GetArguments().Contains(csv)));
    }

    [Fact]
    public async Task ImportItunesLibrary_ValidatesTheFileAndPassesTheOptions()
    {
        var xml = TempFile("Library.xml");
        var notXml = TempFile("Library.plist");

        (await _client.PostAsync($"/api/music/file/{Encode(notXml)}/import-itunes-library", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsync($"/api/music/file/{Encode("/nonexistent/x.xml")}/import-itunes-library", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsync($"/api/music/file/{Encode(xml)}/import-itunes-library?importMissingTracks=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitFor(() => _factory.ItunesImporter.ReceivedCalls().Any());
        await _factory.ItunesImporter.Received(1).Import(xml, 1, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MonitoredFolderScan_ReportsWhetherAScanStarted()
    {
        var response = await _client.PostAsync("/api/monitored-folders/scan", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBeOneOf("true", "false");
    }

    [Fact]
    public async Task ReloadMetadata_ReturnsOkOrNotFound()
    {
        _factory.MusicScanner.ReloadTrackMetadata(601, Arg.Any<CancellationToken>()).Returns(true);

        (await _client.PostAsync("/api/music/601/reload-metadata", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsync("/api/music/602/reload-metadata", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RecentlyAdded_AsksForTheLastWeek()
    {
        _factory.TrackRepository.GetRecentlyAdded(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new RecentlyAddedDto());

        (await _client.GetAsync("/api/music/recently-added")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await _factory.TrackRepository.Received().GetRecentlyAdded(
            Arg.Is<DateTime>(d => Math.Abs((DateTime.UtcNow.AddDays(-7) - d).TotalMinutes) < 5), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1000, 200)]
    public async Task RecentlyPlayed_ClampsTake(int requested, int expected)
    {
        await _client.GetAsync($"/api/music/recently-played?take={requested}");

        await _factory.TrackRepository.Received().GetRecentlyPlayed(expected, Arg.Any<CancellationToken>());
    }

    // ---- favorites ----

    [Fact]
    public async Task AlbumFavorite_GetAddRemove()
    {
        _factory.AlbumRepository.IsAlbumFavorite(701, 1, Arg.Any<CancellationToken>()).Returns(true);

        (await _client.GetFromJsonAsync<bool>("/api/albums/701/favorite")).ShouldBeTrue();
        (await _client.PutAsync("/api/albums/701/favorite", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.DeleteAsync("/api/albums/701/favorite")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await _factory.AlbumRepository.Received(1).SetAlbumFavorite(701, 1, true, Arg.Any<CancellationToken>());
        await _factory.AlbumRepository.Received(1).SetAlbumFavorite(701, 1, false, Arg.Any<CancellationToken>());
    }

    // ---- album cover upload ----

    private static MultipartFormDataContent ImageForm(byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "cover");

        return form;
    }

    [Fact]
    public async Task UploadAlbumCover_WebpImage_IsStoredAsWebp()
    {
        _factory.AlbumRepository.SetCoverArt(711, Arg.Any<string>(), Arg.Any<string>(), "image/webp", Arg.Any<CancellationToken>())
            .Returns(true);
        byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 1, 2];

        var response = await _client.PostAsync("/api/albums/711/cover", ImageForm(webp));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.AlbumRepository.Received(1).SetCoverArt(
            711, Arg.Is<string>(p => p!.EndsWith(".webp")), Arg.Any<string>(), "image/webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAlbumCover_EmptyOrOversizedFile_IsRejected()
    {
        (await _client.PostAsync("/api/albums/712/cover", ImageForm([]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsync("/api/albums/712/cover", ImageForm(new byte[(15 * 1024 * 1024) + 1]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await _factory.AlbumRepository.DidNotReceive().SetCoverArt(
            712, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- cover serving ----

    [Fact]
    public async Task Cover_MissingNonImageOrEmpty_FallsBackToTheDefaultJpeg()
    {
        var empty = Path.Combine(_factory.ImagesPath, "empty.png");
        await File.WriteAllBytesAsync(empty, []);
        _factory.AlbumRepository.GetCoverArt(721, Arg.Any<CancellationToken>()).Returns((CoverArtDto?)null);
        _factory.AlbumRepository.GetCoverArt(722, Arg.Any<CancellationToken>())
            .Returns(new CoverArtDto { Path = "x.txt", Hash = "H1", MimeType = "text/plain" });
        _factory.AlbumRepository.GetCoverArt(723, Arg.Any<CancellationToken>())
            .Returns(new CoverArtDto { Path = "gone.png", Hash = "H2", MimeType = "image/png" });
        _factory.AlbumRepository.GetCoverArt(724, Arg.Any<CancellationToken>())
            .Returns(new CoverArtDto { Path = "empty.png", Hash = "H3", MimeType = "image/png" });

        foreach (var id in new[] { 721, 722, 723, 724 })
        {
            var response = await _client.GetAsync($"/api/cover/album/{id}");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
            response.Headers.ETag!.Tag.ShouldNotBeOneOf("\"H1\"", "\"H2\"", "\"H3\"");
        }
    }

    [Fact]
    public async Task Cover_ServesAbsoluteAndImagesFolderRelativePaths()
    {
        var absolute = TempFile("folder-cover.jpg", "jpeg-bytes");
        await File.WriteAllTextAsync(Path.Combine(_factory.ImagesPath, "stored.webp"), "webp-bytes");
        _factory.TrackRepository.GetCoverArt(731, Arg.Any<CancellationToken>())
            .Returns(new CoverArtDto { Path = absolute, Hash = "ABS", MimeType = "image/jpeg" });
        _factory.TrackRepository.GetCoverArt(732, Arg.Any<CancellationToken>())
            .Returns(new CoverArtDto { Path = "stored.webp", Hash = "REL", MimeType = "image/webp" });

        var first = await _client.GetAsync("/api/cover/track/731");
        var second = await _client.GetAsync("/api/cover/track/732");

        (await first.Content.ReadAsStringAsync()).ShouldBe("jpeg-bytes");
        first.Headers.ETag!.Tag.ShouldBe("\"ABS\"");
        (await second.Content.ReadAsStringAsync()).ShouldBe("webp-bytes");
        second.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
    }

    // ---- artists / Discogs ----

    [Theory]
    [InlineData("a.png", "image/png")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.jpeg", "image/jpeg")]
    public async Task DiscogsImage_IsServedWithAMimeTypeFromItsExtension(string fileName, string mimeType)
    {
        var stored = $"{Guid.NewGuid():N}-{fileName}";
        await File.WriteAllTextAsync(Path.Combine(_factory.ImagesPath, stored), "img");
        var artistId = (uint)(800 + mimeType.Length + fileName.Length);
        _factory.ArtistRepository.GetDiscogsImagePath(artistId, Arg.Any<CancellationToken>()).Returns(stored);

        var response = await _client.GetAsync($"/api/artists/{artistId}/discogs-image");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(mimeType);
    }

    [Fact]
    public async Task DiscogsImage_FileMissingOnDisk_ReturnsNoContent()
    {
        _factory.ArtistRepository.GetDiscogsImagePath(851, Arg.Any<CancellationToken>()).Returns("missing.jpg");

        (await _client.GetAsync("/api/artists/851/discogs-image")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DiscogsRefreshAndForce_QueueEnrichmentWork()
    {
        (await _client.PostAsync("/api/artists/861/discogs/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync("/api/artists/862/discogs/force", new ForceDiscogsArtistRequest(4242))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await _factory.ArtistRepository.Received().ResetToNotAttempted(861, Arg.Any<CancellationToken>());
        await _factory.ArtistRepository.Received().ResetToNotAttempted(862, Arg.Any<CancellationToken>());
    }

    // ---- users ----

    [Fact]
    public async Task CreateUser_UsernameTooShortAfterTrimming_IsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest { Username = " ab  ", Password = "password123" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _factory.UserRepository.DidNotReceive().CreateUser("ab", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CurrentUserAndLoudnessSetting_RequireAKnownUser()
    {
        _factory.UserRepository.GetCurrentUser(1, Arg.Any<CancellationToken>()).Returns(
            (CurrentUserDto?)null,
            (CurrentUserDto?)null,
            new CurrentUserDto("testuser", true, false),
            new CurrentUserDto("testuser", true, false));

        (await _client.GetAsync("/api/users/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PutAsJsonAsync("/api/users/me/loudness-normalization", new SetLoudnessNormalizationRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetFromJsonAsync<CurrentUserDto>("/api/users/me"))!.Username.ShouldBe("testuser");
        (await _client.PutAsJsonAsync("/api/users/me/loudness-normalization", new SetLoudnessNormalizationRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await _factory.UserRepository.Received(1).SetLoudnessNormalizationEnabled("testuser", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeOwnPassword_UnknownCallerOrMissingHash_IsUnauthorized()
    {
        _factory.UserRepository.GetById(1, Arg.Any<CancellationToken>()).Returns(
            (UserSummaryDto?)null,
            new UserSummaryDto(1, "hashless", true));
        _factory.UserRepository.GetPasswordHash("hashless", Arg.Any<CancellationToken>()).Returns((string?)null);
        var request = new ChangePasswordRequest("old-password", "new-password");

        (await _client.PutAsJsonAsync("/api/users/me/password", request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PutAsJsonAsync("/api/users/me/password", request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await _factory.UserRepository.DidNotReceive().UpdateHashedPassword("hashless", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserAdmin_RevokingAnUnknownUser_IsNotFound()
    {
        _factory.UserRepository.GetById(1, Arg.Any<CancellationToken>()).Returns((UserSummaryDto?)null);
        _factory.UserRepository.SetIsAdmin("nobody", false, Arg.Any<CancellationToken>()).Returns(false);

        var response = await _client.PutAsJsonAsync("/api/users/nobody/admin", new SetAdminRequest(false));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await _factory.UserRepository.DidNotReceive().RevokeAllRefreshTokens("nobody", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    [InlineData(180, null)]
    public async Task SetTrackTrim_PointsOutsideTheTrack_AreRejected(int? startSeconds, int? endSeconds)
    {
        _factory.TrackRepository.GetDuration(901, Arg.Any<CancellationToken>()).Returns(TimeSpan.FromMinutes(3));
        var request = new SetTrackTrimRequest(
            startSeconds is { } s ? TimeSpan.FromSeconds(s) : null,
            endSeconds is { } e ? TimeSpan.FromSeconds(e) : null);

        (await _client.PutAsJsonAsync("/api/music/901/trim", request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnrichArtist_WhenEnrichmentRuns_ReturnsTheRefreshedArtist()
    {
        _factory.ArtistRepository.GetArtist(871, Arg.Any<CancellationToken>()).Returns(
            new ArtistDetailsDto { Id = 871, Name = "Before", DiscogsLookupStatus = ArtistDiscogsLookupStatus.NotAttempted },
            new ArtistDetailsDto { Id = 871, Name = "After", DiscogsLookupStatus = ArtistDiscogsLookupStatus.Found });
        _factory.ArtistRepository.TryClaimForEnrichment(871, Arg.Any<CancellationToken>()).Returns(true);
        _factory.ArtistRepository.GetArtistName(871, Arg.Any<CancellationToken>()).Returns((string?)null);

        var response = await _client.PostAsync("/api/artists/871/enrich", null);

        (await response.Content.ReadFromJsonAsync<ArtistDetailsDto>())!.Name.ShouldBe("After");
    }

    // ---- state ----

    [Fact]
    public async Task ClearState_RemovesTheSavedPlaybackState()
    {
        _factory.TrackRepository.GetDuration(Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TimeSpan.FromMinutes(3));
        await _client.PostAsJsonAsync("/api/state/playlist", new PlaylistState { CurrentTrack = 1, TrackIds = [1, 2] });
        (await _client.GetAsync("/api/state")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _client.DeleteAsync("/api/state")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _client.GetAsync("/api/state")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }
}
