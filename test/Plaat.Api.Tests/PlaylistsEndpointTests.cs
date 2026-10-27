using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class PlaylistsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateSmartPlaylist_WithNestedGroupAndLimit_CreatesAndReturnsCreatedPlaylist()
    {
        var request = new CreateSmartPlaylistRequest
        {
            RootGroup = new SmartPlaylistRuleGroupDto
            {
                Combinator = SmartPlaylistGroupCombinator.Or,
                Rules = [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 5 }],
                ChildGroups =
                [
                    new SmartPlaylistRuleGroupDto
                    {
                        Combinator = SmartPlaylistGroupCombinator.And,
                        Rules = [new GenrePlaylistRuleDto { TextMatchType = TextMatchType.Equals, Value = "Rock" }]
                    }
                ]
            },
            Limit = 25,
            LimitOrderBy = SmartPlaylistLimitOrderByDto.RecentlyAdded
        };

        factory.PlaylistRepository
            .GetPlaylist("Nested Smart Playlist", 1, Arg.Any<CancellationToken>())
            .Returns(new SmartPlaylistDetailsDto
            {
                Id = 1,
                PlaylistName = "Nested Smart Playlist",
                IsSmart = true,
                IsPinned = false,
                TrackCount = 7,
                Tracks = []
            });

        var createResponse = await _client.PostAsJsonAsync("/api/playlists/smart/Nested Smart Playlist", request);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.PlaylistRepository.Received(1).CreateSmartPlaylist(
            "Nested Smart Playlist",
            1,
            Arg.Is<SmartPlaylistRuleGroupDto>(g =>
                g != null &&
                g.Combinator == SmartPlaylistGroupCombinator.Or &&
                g.Rules.Count == 1 &&
                g.ChildGroups.Count == 1),
            25,
            SmartPlaylistLimitOrderByDto.RecentlyAdded,
            Arg.Any<CancellationToken>());

        var getResponse = await _client.GetAsync("/api/playlists/Nested Smart Playlist");

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await getResponse.Content.ReadFromJsonAsync<SmartPlaylistDetailsDto>();
        result.ShouldNotBeNull();
        result.TrackCount.ShouldBe(7);
        result.IsSmart.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSmartPlaylist_DuplicateName_ReturnsConflict()
    {
        var request = new CreateSmartPlaylistRequest
        {
            RootGroup = new SmartPlaylistRuleGroupDto
            {
                Rules = [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 1 }]
            }
        };

        factory.PlaylistRepository
            .CreateSmartPlaylist(
                "Duplicate",
                Arg.Any<int>(),
                Arg.Any<SmartPlaylistRuleGroupDto>(),
                Arg.Any<int?>(),
                Arg.Any<SmartPlaylistLimitOrderByDto?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new System.Data.DuplicateNameException("Playlist already exists"));

        var response = await _client.PostAsJsonAsync("/api/playlists/smart/Duplicate", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DuplicatePlaylist_TrimsNameAndPassesItToTheRepository()
    {
        factory.PlaylistRepository
            .DuplicatePlaylist(7, false, 1, "My list (copy)", Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PostAsJsonAsync(
            "/api/playlists/7/duplicate", new DuplicatePlaylistRequest(false, "  My list (copy)  "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.PlaylistRepository.Received(1).DuplicatePlaylist(
            7, false, 1, "My list (copy)", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicatePlaylist_WithBlankName_ReturnsBadRequestAndDoesNotTouchTheRepository()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/playlists/42/duplicate", new DuplicatePlaylistRequest(false, "   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The repository substitute is shared by every test in this class (it lives on the class
        // fixture), so this has to be scoped to this test's own playlist id.
        await factory.PlaylistRepository.DidNotReceive().DuplicatePlaylist(
            42, Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicatePlaylist_WithNameAlreadyInUse_ReturnsConflict()
    {
        factory.PlaylistRepository
            .DuplicatePlaylist(8, true, 1, "Taken", Arg.Any<CancellationToken>())
            .ThrowsAsync(new System.Data.DuplicateNameException("Playlist already exists"));

        var response = await _client.PostAsJsonAsync(
            "/api/playlists/8/duplicate", new DuplicatePlaylistRequest(true, "Taken"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DuplicatePlaylist_ForUnknownPlaylist_ReturnsNotFound()
    {
        factory.PlaylistRepository
            .DuplicatePlaylist(999, false, 1, "Copy", Arg.Any<CancellationToken>())
            .Returns(false);

        var response = await _client.PostAsJsonAsync(
            "/api/playlists/999/duplicate", new DuplicatePlaylistRequest(false, "Copy"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
