using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

/// <summary>
/// Exercises <see cref="IPlaatClient"/> through its public DI registration against a stub primary
/// handler, so every request's URL/method/body and every response-status branch is checked without
/// a real server.
/// </summary>
public sealed class PlaatClientTests : IDisposable
{
    private readonly StubHandler _handler = new();
    private readonly TokenStorageService _tokens = new();
    private readonly ITokenSessionStore _sessionStore = Substitute.For<ITokenSessionStore>();
    private readonly ServiceProvider _provider;
    private readonly IPlaatClient _client;

    public PlaatClientTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tokens);
        services.AddSingleton(_sessionStore);
        services.AddPlaatClient(
            new PlaatBackendOptions { BaseUri = new Uri("http://plaat.test/") },
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => _handler));

        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<IPlaatClient>();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static CancellationToken Ct
    {
        get
        {
            return CancellationToken.None;
        }
    }

    // ---- auth / token refresh ----

    [Fact]
    public async Task Login_WithClientIp_SendsForwardedForHeaderAndReturnsToken()
    {
        _handler.RespondJson("""{"accessToken":"a","refreshToken":"r","expiresAt":"2030-01-01T00:00:00Z"}""");

        var result = await _client.Login("user", "pass", "1.2.3.4", Ct);

        result.ShouldNotBeNull();
        result.AccessToken.ShouldBe("a");
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Path.ShouldBe("/api/auth/login");
        request.Headers.GetValues("X-Forwarded-For").ShouldBe(["1.2.3.4"]);
        request.Body.ShouldContain("\"username\":\"user\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Login_WithoutClientIp_OmitsForwardedForHeader(string? clientIp)
    {
        _handler.RespondJson("""{"accessToken":"a","refreshToken":"r","expiresAt":"2030-01-01T00:00:00Z"}""");

        await _client.Login("user", "pass", clientIp, Ct);

        _handler.Requests.ShouldHaveSingleItem().Headers.Contains("X-Forwarded-For").ShouldBeFalse();
    }

    [Fact]
    public async Task Login_WhenUnauthorized_Throws()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);

        await Should.ThrowAsync<HttpRequestException>(() => _client.Login("user", "bad", null, Ct));
    }

    [Fact]
    public async Task RefreshToken_PostsRefreshTokenAndReturnsNewPair()
    {
        _handler.RespondJson("""{"accessToken":"a2","refreshToken":"r2","expiresAt":"2030-01-01T00:00:00Z"}""");

        var result = await _client.RefreshToken("r1", Ct);

        result!.RefreshToken.ShouldBe("r2");
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/api/auth/refresh");
        request.Body.ShouldContain("\"refreshToken\":\"r1\"");
    }

    [Fact]
    public async Task GetAppCompatibility_OnSuccess_ReturnsDto()
    {
        _handler.RespondJson("""{"minimumAppVersion":"1.0.0","maximumAppVersion":null}""");

        var result = await _client.GetAppCompatibility(Ct);

        result.ShouldNotBeNull();
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/app/compatibility");
    }

    [Fact]
    public async Task GetAppCompatibility_OnFailure_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        (await _client.GetAppCompatibility(Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task AuthenticatedCall_WithoutAccessToken_SendsNoAuthorizationHeader()
    {
        _handler.RespondJson("[]");

        await _client.GetAlbums(Ct);

        _handler.Requests.ShouldHaveSingleItem().Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task AuthenticatedCall_WithFreshToken_SendsBearerWithoutRefreshing()
    {
        _tokens.AccessToken = "access";
        _tokens.RefreshToken = "refresh";
        _tokens.ExpiresAt = DateTime.UtcNow.AddHours(1);
        _handler.RespondJson("[]");

        await _client.GetAlbums(Ct);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBe("Bearer access");
        await _sessionStore.DidNotReceiveWithAnyArgs().PersistRefreshedToken(null!, CancellationToken.None);
    }

    [Fact]
    public async Task AuthenticatedCall_WithoutRefreshToken_DoesNotRefreshEvenWhenExpired()
    {
        _tokens.AccessToken = "access";
        _tokens.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        _handler.RespondJson("[]");

        await _client.GetAlbums(Ct);

        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/albums");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedCall_WithExpiringOrUnknownExpiry_RefreshesAndPersistsFirst(bool expiryUnknown)
    {
        _tokens.AccessToken = "old";
        _tokens.RefreshToken = "refresh";
        _tokens.ExpiresAt = expiryUnknown ? null : DateTime.UtcNow.AddMinutes(1);
        _sessionStore
            .When(s => s.PersistRefreshedToken(Arg.Any<TokenResponse>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var response = call.Arg<TokenResponse>()!;
                _tokens.AccessToken = response.AccessToken;
                _tokens.RefreshToken = response.RefreshToken;
                _tokens.ExpiresAt = response.ExpiresAt;
            });
        _handler.RespondJson("""{"accessToken":"new","refreshToken":"r2","expiresAt":"2099-01-01T00:00:00Z"}""");
        _handler.RespondJson("[]");

        await _client.GetAlbums(Ct);

        _handler.Requests.Count.ShouldBe(2);
        _handler.Requests[0].Path.ShouldBe("/api/auth/refresh");
        _handler.Requests[1].Authorization.ShouldBe("Bearer new");
        await _sessionStore.Received(1).PersistRefreshedToken(
            Arg.Is<TokenResponse>(t => t!.AccessToken == "new"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthenticatedCall_WhenRefreshReturnsNullBody_DoesNotPersistAndUsesOldToken()
    {
        _tokens.AccessToken = "old";
        _tokens.RefreshToken = "refresh";
        _tokens.ExpiresAt = null;
        _handler.RespondJson("null");
        _handler.RespondJson("[]");

        await _client.GetAlbums(Ct);

        _handler.Requests[1].Authorization.ShouldBe("Bearer old");
        await _sessionStore.DidNotReceiveWithAnyArgs().PersistRefreshedToken(null!, CancellationToken.None);
    }

    [Fact]
    public async Task AuthenticatedCall_WhenRefreshFails_SwallowsErrorAndProceedsWithOldToken()
    {
        _tokens.AccessToken = "old";
        _tokens.RefreshToken = "refresh";
        _tokens.ExpiresAt = null;
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.RespondJson("[]");

        var result = await _client.GetAlbums(Ct);

        result.ShouldBeEmpty();
        _handler.Requests[1].Authorization.ShouldBe("Bearer old");
    }

    [Fact]
    public async Task ConcurrentCalls_NeedingRefresh_OnlyRefreshOnce()
    {
        _tokens.AccessToken = "old";
        _tokens.RefreshToken = "refresh";
        _tokens.ExpiresAt = null;
        _sessionStore
            .When(s => s.PersistRefreshedToken(Arg.Any<TokenResponse>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var response = call.Arg<TokenResponse>()!;
                _tokens.AccessToken = response.AccessToken;
                _tokens.ExpiresAt = response.ExpiresAt;
            });
        var releaseRefresh = new TaskCompletionSource();
        var refreshStarted = new TaskCompletionSource();
        _handler.Handler = async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
            {
                refreshStarted.TrySetResult();
                await releaseRefresh.Task;
                return StubHandler.Json("""{"accessToken":"new","refreshToken":"r2","expiresAt":"2099-01-01T00:00:00Z"}""");
            }

            return StubHandler.Json("[]");
        };

        var first = _client.GetAlbums(Ct);
        await refreshStarted.Task;
        var second = _client.GetArtists(Ct);
        releaseRefresh.SetResult();
        await Task.WhenAll(first, second);

        _handler.Requests.Count(r => r.Path == "/api/auth/refresh").ShouldBe(1);
        _handler.Requests.Where(r => r.Path != "/api/auth/refresh").ShouldAllBe(r => r.Authorization == "Bearer new");
    }

    // ---- tracks ----

    [Fact]
    public async Task GetTracksPage_WithSearch_BuildsFullQueryString()
    {
        _handler.RespondJson("""{"items":[],"totalCount":7}""");

        var result = await _client.GetTracksPage(new TrackQuery("a b", TrackSortField.Title, true, 10, 20), Ct);

        result.TotalCount.ShouldBe(7);
        _handler.Requests.ShouldHaveSingleItem().PathAndQuery
            .ShouldBe("/api/music?search=a%20b&sort=Title&desc=true&skip=10&take=20");
    }

    [Fact]
    public async Task GetTracksPage_WithoutSearch_OmitsSearchAndDefaultsOnNullBody()
    {
        _handler.RespondJson("null");

        var result = await _client.GetTracksPage(new TrackQuery(" "), Ct);

        result.Items.ShouldBeEmpty();
        _handler.Requests.ShouldHaveSingleItem().PathAndQuery
            .ShouldBe("/api/music?sort=Title&desc=false&skip=0&take=100");
    }

    [Fact]
    public async Task GetTrackIds_ReturnsIdsAndEmptyOnNull()
    {
        _handler.RespondJson("[3,1]");
        _handler.RespondJson("null");

        (await _client.GetTrackIds(new TrackQuery(), Ct)).ShouldBe([3u, 1u]);
        (await _client.GetTrackIds(new TrackQuery(), Ct)).ShouldBeEmpty();
        _handler.Requests[0].PathAndQuery.ShouldBe("/api/music/ids?sort=Title&desc=false");
    }

    [Fact]
    public async Task GetTracksByIds_WithNoIds_DoesNotCallServer()
    {
        (await _client.GetTracksByIds([], Ct)).ShouldBeEmpty();

        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTracksByIds_PostsIdsAndHandlesNullBody()
    {
        _handler.RespondJson("[]");
        _handler.RespondJson("null");

        (await _client.GetTracksByIds([1, 2], Ct)).ShouldBeEmpty();
        (await _client.GetTracksByIds([1], Ct)).ShouldBeEmpty();

        _handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        _handler.Requests[0].Path.ShouldBe("/api/music/by-ids");
        _handler.Requests[0].Body.ShouldBe("[1,2]");
    }

    [Fact]
    public async Task GetTracksByIds_OnError_Throws()
    {
        _handler.Respond(HttpStatusCode.InternalServerError);

        await Should.ThrowAsync<HttpRequestException>(() => _client.GetTracksByIds([1], Ct));
    }

    [Fact]
    public async Task Search_EscapesQueryAndDefaultsOnNull()
    {
        _handler.RespondJson("null");

        var result = await _client.Search("a&b", Ct);

        result.ShouldNotBeNull();
        _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe("/api/search?q=a%26b");
    }

    [Fact]
    public async Task GetStats_SendsRangeAndScopeAndDefaultsOnNull()
    {
        _handler.RespondJson("null");

        var result = await _client.GetStats(StatsRange.All, StatsScope.Me, Ct);

        result.Library.TrackCount.ShouldBe(0);
        _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe($"/api/stats?range={StatsRange.All}&scope={StatsScope.Me}");
    }

    [Fact]
    public async Task GetTrackDetails_WhenNoContent_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        (await _client.GetTrackDetails(5, Ct)).ShouldBeNull();
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/music/5/details");
    }

    [Fact]
    public async Task GetTrackDetails_WhenNotFound_Throws()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        await Should.ThrowAsync<HttpRequestException>(() => _client.GetTrackDetails(5, Ct));
    }

    [Fact]
    public async Task SetDefaultTrackLocation_PutsToLocationUrl()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        await _client.SetDefaultTrackLocation(1, 2, Ct);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Put);
        request.Path.ShouldBe("/api/music/1/locations/2/default");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task SetTrackTrim_MapsStatus(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.SetTrackTrim(1, TimeSpan.FromSeconds(1), null, Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/music/1/trim");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task ReloadTrackMetadata_MapsStatus(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.ReloadTrackMetadata(1, Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/music/1/reload-metadata");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task SetTrackRating_MapsStatus(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.SetTrackRating(1, 4, Ct)).ShouldBe(expected);
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/api/ratings/tracks/1");
        request.Body.ShouldBe("""{"stars":4}""");
    }

    [Fact]
    public async Task GetTrackRating_ReturnsStarsOrMinimumOnNull()
    {
        _handler.RespondJson("""{"trackId":1,"stars":3}""");
        _handler.RespondJson("null");

        (await _client.GetTrackRating(1, Ct)).ShouldBe(3);
        (await _client.GetTrackRating(1, Ct)).ShouldBe(TrackRatingDto.MinStars);
    }

    // ---- simple GET collections (value + null-body fallback) ----

    public static TheoryData<string, string> CollectionGets()
    {
        return new TheoryData<string, string>
        {
            { nameof(IPlaatClient.GetRecentlyPlayed), "/api/music/recently-played" },
            { nameof(IPlaatClient.GetPossiblyCorruptTracks), "/api/music/possibly-corrupt" },
            { nameof(IPlaatClient.GetAlbums), "/api/albums" },
            { nameof(IPlaatClient.GetFavoriteAlbums), "/api/favorites/albums" },
            { nameof(IPlaatClient.GetArtists), "/api/artists" },
            { nameof(IPlaatClient.GetFavoriteArtists), "/api/favorites/artists" },
            { nameof(IPlaatClient.GetPlaylists), "/api/playlists" },
            { nameof(IPlaatClient.GetUsers), "/api/users" },
            { nameof(IPlaatClient.GetMonitoredFolders), "/api/monitored-folders" }
        };
    }

    [Theory]
    [MemberData(nameof(CollectionGets))]
    public async Task CollectionGet_ReturnsEmptyForEmptyAndNullBodies(string method, string path)
    {
        _handler.RespondJson("[]");
        _handler.RespondJson("null");

        var call = CollectionCall(method);
        (await call()).ShouldBe(0);
        (await call()).ShouldBe(0);

        _handler.Requests.ShouldAllBe(r => r.Path == path && r.Method == HttpMethod.Get);
    }

    private Func<Task<int>> CollectionCall(string method)
    {
        return method switch
        {
            nameof(IPlaatClient.GetRecentlyPlayed) => async () => (await _client.GetRecentlyPlayed(Ct)).Count,
            nameof(IPlaatClient.GetPossiblyCorruptTracks) => async () => (await _client.GetPossiblyCorruptTracks(Ct)).Count,
            nameof(IPlaatClient.GetAlbums) => async () => (await _client.GetAlbums(Ct)).Count,
            nameof(IPlaatClient.GetFavoriteAlbums) => async () => (await _client.GetFavoriteAlbums(Ct)).Count,
            nameof(IPlaatClient.GetArtists) => async () => (await _client.GetArtists(Ct)).Count,
            nameof(IPlaatClient.GetFavoriteArtists) => async () => (await _client.GetFavoriteArtists(Ct)).Count,
            nameof(IPlaatClient.GetPlaylists) => async () => (await _client.GetPlaylists(Ct)).Count,
            nameof(IPlaatClient.GetUsers) => async () => (await _client.GetUsers(Ct)).Count,
            nameof(IPlaatClient.GetMonitoredFolders) => async () => (await _client.GetMonitoredFolders(Ct)).Count,
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    [Fact]
    public async Task GetRecentlyAdded_DefaultsOnNull()
    {
        _handler.RespondJson("null");

        (await _client.GetRecentlyAdded(Ct)).ShouldNotBeNull();
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/music/recently-added");
    }

    // ---- albums / artists ----

    [Fact]
    public async Task GetAlbum_GetArtist_GetCurrentState_GetCurrentUser_HitExpectedUrls()
    {
        _handler.RespondJson("null");
        _handler.RespondJson("null");
        _handler.RespondJson("null");
        _handler.RespondJson("null");

        (await _client.GetAlbum(4, Ct)).ShouldBeNull();
        (await _client.GetArtist(5, Ct)).ShouldBeNull();
        (await _client.GetCurrentState(Ct)).ShouldBeNull();
        (await _client.GetCurrentUser(Ct)).ShouldBeNull();

        _handler.Requests.Select(r => r.Path).ShouldBe(["/api/albums/4", "/api/artists/5", "/api/state", "/api/users/me"]);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task IsAlbumFavorite_And_IsArtistFavorite_ReadBooleanBody(string body, bool expected)
    {
        _handler.RespondJson(body);
        _handler.RespondJson(body);

        (await _client.IsAlbumFavorite(1, Ct)).ShouldBe(expected);
        (await _client.IsArtistFavorite(2, Ct)).ShouldBe(expected);

        _handler.Requests.Select(r => r.Path).ShouldBe(["/api/albums/1/favorite", "/api/artists/2/favorite"]);
    }

    [Theory]
    [InlineData(true, "PUT")]
    [InlineData(false, "DELETE")]
    public async Task SetAlbumFavorite_And_SetArtistFavorite_UsePutOrDelete(bool isFavorite, string method)
    {
        _handler.Respond(HttpStatusCode.NoContent);
        _handler.Respond(HttpStatusCode.NoContent);

        await _client.SetAlbumFavorite(1, isFavorite, Ct);
        await _client.SetArtistFavorite(2, isFavorite, Ct);

        _handler.Requests.ShouldAllBe(r => r.Method.Method == method);
        _handler.Requests.Select(r => r.Path).ShouldBe(["/api/albums/1/favorite", "/api/artists/2/favorite"]);
    }

    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData(" ", "application/octet-stream")]
    public async Task UploadAlbumCover_SendsMultipartWithContentType(string contentType, string expectedContentType)
    {
        _handler.Respond(HttpStatusCode.NoContent);

        var result = await _client.UploadAlbumCover(3, new MemoryStream([1, 2, 3]), contentType, Ct);

        result.ShouldBeTrue();
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/api/albums/3/cover");
        request.Body.ShouldContain($"Content-Type: {expectedContentType}");
        request.Body.ShouldContain("name=file");
    }

    [Fact]
    public async Task UploadAlbumCover_WhenAlbumMissing_ReturnsFalse()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        (await _client.UploadAlbumCover(3, new MemoryStream(), "image/png", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task EnrichArtist_WhenNoContent_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        (await _client.EnrichArtist(9, Ct)).ShouldBeNull();
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/artists/9/enrich");
    }

    [Fact]
    public async Task EnrichArtist_OnSuccess_ReadsBody()
    {
        _handler.RespondJson("null");

        (await _client.EnrichArtist(9, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task EnrichArtist_OnError_Throws()
    {
        _handler.Respond(HttpStatusCode.InternalServerError);

        await Should.ThrowAsync<HttpRequestException>(() => _client.EnrichArtist(9, Ct));
    }

    [Fact]
    public async Task ArtistDiscogsCalls_PostToExpectedUrls()
    {
        _handler.Respond(HttpStatusCode.NoContent);
        _handler.Respond(HttpStatusCode.NoContent);

        await _client.RefreshArtistDiscogsInfo(1, Ct);
        await _client.ForceArtistDiscogsId(1, 42, Ct);

        _handler.Requests.Select(r => r.Path).ShouldBe(["/api/artists/1/discogs/refresh", "/api/artists/1/discogs/force"]);
        _handler.Requests[1].Body.ShouldContain("42");
    }

    // ---- state ----

    [Fact]
    public async Task StateCalls_UseExpectedMethodsAndUrls()
    {
        _handler.Respond(HttpStatusCode.NoContent);
        _handler.Respond(HttpStatusCode.NoContent);
        _handler.Respond(HttpStatusCode.NoContent);

        await _client.UpdateCurrentTrackPlayback(new TrackPlaybackState { CurrentTrack = 1, CurrentSecond = 2 }, Ct);
        await _client.UpdateCurrentPlaylist(new PlaylistState { CurrentTrack = 1, TrackIds = [1, 2] }, Ct);
        await _client.ClearCurrentState(Ct);

        _handler.Requests.Select(r => $"{r.Method} {r.Path}")
            .ShouldBe(["POST /api/state/track", "POST /api/state/playlist", "DELETE /api/state"]);
    }

    // ---- playlists ----

    [Fact]
    public async Task GetPlaylist_EscapesName()
    {
        _handler.RespondJson("null");

        await _client.GetPlaylist("my list/1", Ct);

        _handler.Requests.ShouldHaveSingleItem().RawPath.ShouldBe("/api/playlists/my%20list%2F1");
    }

    [Fact]
    public async Task PlaylistMutations_UseExpectedMethodsUrlsAndBodies()
    {
        for (var i = 0; i < 5; i++)
        {
            _handler.Respond(HttpStatusCode.NoContent);
        }

        await _client.AddToPlaylist(1, 2, Ct);
        await _client.RemoveFromPlaylist(1, 2, Ct);
        await _client.ReorderPlaylist(1, [3, 2], Ct);
        await _client.DeletePlaylist(1, true, Ct);
        await _client.SetPlaylistPinned(1, false, true, Ct);

        _handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").ShouldBe([
            "PUT /api/playlists/1",
            "DELETE /api/playlists/1/tracks/2",
            "PUT /api/playlists/1/order",
            "DELETE /api/playlists/1?isSmart=True",
            "PUT /api/playlists/1/pin"
        ]);
        _handler.Requests[0].Body.ShouldBe("[2]");
        _handler.Requests[2].Body.ShouldBe("[3,2]");
        _handler.Requests[4].Body.ShouldBe("""{"isSmart":false,"isPinned":true}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    public async Task CreatePlaylist_MapsConflictToFalse(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.CreatePlaylist("a b", Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().RawPath.ShouldBe("/api/playlists/a%20b");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    public async Task CreateSmartPlaylist_MapsConflictToFalse(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.CreateSmartPlaylist("s", SmartRequest(), Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/playlists/smart/s");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task UpdateSmartPlaylist_MapsNotFoundToFalse(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.UpdateSmartPlaylist(7, SmartRequest(), Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/playlists/smart/7");
    }

    [Fact]
    public async Task GetSmartPlaylistRules_HitsRulesUrl()
    {
        _handler.RespondJson("null");

        (await _client.GetSmartPlaylistRules(7, Ct)).ShouldBeNull();
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/playlists/smart/7/rules");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    public async Task DuplicatePlaylist_MapsConflictToFalse(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.DuplicatePlaylist(1, false, "copy", Ct)).ShouldBe(expected);
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/api/playlists/1/duplicate");
        request.Body.ShouldContain("\"newName\":\"copy\"");
    }

    [Fact]
    public async Task ConflictMappedCalls_StillThrowOnOtherErrors()
    {
        _handler.Respond(HttpStatusCode.BadRequest);
        _handler.Respond(HttpStatusCode.BadRequest);
        _handler.Respond(HttpStatusCode.BadRequest);
        _handler.Respond(HttpStatusCode.BadRequest);

        await Should.ThrowAsync<HttpRequestException>(() => _client.CreatePlaylist("x", Ct));
        await Should.ThrowAsync<HttpRequestException>(() => _client.CreateSmartPlaylist("x", SmartRequest(), Ct));
        await Should.ThrowAsync<HttpRequestException>(() => _client.DuplicatePlaylist(1, false, "x", Ct));
        await Should.ThrowAsync<HttpRequestException>(() => _client.AddMonitoredFolder("/x", Ct));
    }

    // ---- users ----

    [Fact]
    public async Task UserCalls_UseExpectedMethodsAndUrls()
    {
        for (var i = 0; i < 4; i++)
        {
            _handler.Respond(HttpStatusCode.NoContent);
        }

        await _client.SetLoudnessNormalization(true, Ct);
        await _client.CreateUser(new CreateUserRequest { Username = "bob", Password = "password1" }, Ct);
        await _client.SetUserPassword("a b", new SetPasswordRequest("password1"), Ct);
        await _client.SetUserAdmin("a b", true, Ct);

        _handler.Requests.Select(r => $"{r.Method} {r.RawPath}").ShouldBe([
            "PUT /api/users/me/loudness-normalization",
            "POST /api/users",
            "PUT /api/users/a%20b/password",
            "PUT /api/users/a%20b/admin"
        ]);
        _handler.Requests[3].Body.ShouldBe("""{"isAdmin":true}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task ChangeOwnPassword_MapsUnauthorizedToFalse(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.ChangeOwnPassword(new ChangePasswordRequest("old", "newpassword"), Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/users/me/password");
    }

    [Fact]
    public async Task ChangeOwnPassword_OnBadRequest_Throws()
    {
        _handler.Respond(HttpStatusCode.BadRequest);

        await Should.ThrowAsync<HttpRequestException>(
            () => _client.ChangeOwnPassword(new ChangePasswordRequest("old", "short"), Ct));
    }

    // ---- monitored folders / tasks / logs ----

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    public async Task AddMonitoredFolder_MapsConflictToFalse(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status);

        (await _client.AddMonitoredFolder("/music", Ct)).ShouldBe(expected);
        _handler.Requests.ShouldHaveSingleItem().Body.ShouldBe("""{"path":"/music"}""");
    }

    [Fact]
    public async Task RemoveMonitoredFolder_DeletesById()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        await _client.RemoveMonitoredFolder(4, Ct);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Delete);
        request.Path.ShouldBe("/api/monitored-folders/4");
    }

    [Fact]
    public async Task TaskTriggers_PostAndReadBooleanResult()
    {
        _handler.RespondJson("true");
        _handler.RespondJson("false");
        _handler.RespondJson("true");

        (await _client.TriggerFolderScan(Ct)).ShouldBeTrue();
        (await _client.TriggerConvert(Ct)).ShouldBeFalse();
        (await _client.TriggerLoudnessAnalysis(Ct)).ShouldBeTrue();

        _handler.Requests.Select(r => r.Path).ShouldBe([
            "/api/monitored-folders/scan", "/api/queue/convert", "/api/loudness-analysis/scan"
        ]);
    }

    [Fact]
    public async Task GetTasksStatus_ReturnsIdleDefaultOnNull()
    {
        _handler.RespondJson("null");

        var status = await _client.GetTasksStatus(Ct);

        status.ScanBusy.ShouldBeFalse();
        status.ConvertCurrentItems.ShouldBeEmpty();
        _handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/tasks/status");
    }

    [Fact]
    public async Task GetLogs_WithSearchAndLevels_BuildsAllParameters()
    {
        _handler.RespondJson("[]");

        await _client.GetLogs(new LogQuery(["Error", "Warning"], "x y", 50), Ct);

        _handler.Requests.ShouldHaveSingleItem().PathAndQuery
            .ShouldBe("/api/logs?limit=50&search=x%20y&levels=Error&levels=Warning");
    }

    [Fact]
    public async Task GetLogs_WithoutFilters_OnlySendsLimitAndDefaultsOnNull()
    {
        _handler.RespondJson("null");
        _handler.RespondJson("null");

        (await _client.GetLogs(new LogQuery(), Ct)).ShouldBeEmpty();
        (await _client.GetLogs(new LogQuery([], " "), Ct)).ShouldBeEmpty();

        _handler.Requests.ShouldAllBe(r => r.PathAndQuery == "/api/logs?limit=200");
    }

    private static CreateSmartPlaylistRequest SmartRequest()
    {
        return new CreateSmartPlaylistRequest { RootGroup = new SmartPlaylistRuleGroupDto() };
    }

    /// <summary>
    /// Captured copy of one request, taken while its content is still readable.
    /// </summary>
    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string RawPath,
        string PathAndQuery,
        string? Authorization,
        System.Net.Http.Headers.HttpRequestHeaders Headers,
        string Body);

    /// <summary>
    /// Primary handler returning queued responses in order (or via a custom delegate), recording
    /// every request it sees.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Handler { get; set; }

        public static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public void RespondJson(string json)
        {
            _responses.Enqueue(() => Json(json));
        }

        public void Respond(HttpStatusCode status)
        {
            _responses.Enqueue(() => new HttpResponseMessage(status));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var uri = request.RequestUri!;

            lock (Requests)
            {
                Requests.Add(new RecordedRequest(
                    request.Method,
                    uri.AbsolutePath,
                    uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).Insert(0, "/"),
                    uri.PathAndQuery,
                    request.Headers.Authorization?.ToString(),
                    request.Headers,
                    body));
            }

            if (Handler is not null)
            {
                return await Handler(request, cancellationToken);
            }

            lock (_responses)
            {
                return _responses.Dequeue()();
            }
        }
    }
}
