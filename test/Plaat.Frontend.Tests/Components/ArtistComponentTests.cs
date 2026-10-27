using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components;
using Plaat.Frontend.Components.Pages;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class ArtistComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public ArtistComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new MusicPlaylist());
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
        Services.AddLogging();

        // Stub MusicFileList to avoid needing MudBlazor services
        ComponentFactories.AddStub<MusicFileList>();
    }

    private static ArtistDetailsDto BuildArtist(
        uint id, ArtistDiscogsLookupStatus lookupStatus, string? summary = null)
    {
        return new()
        {
            Id = id,
            Name = "Test Artist",
            Tracks = [],
            Albums = [],
            DiscogsSummary = summary,
            DiscogsLookupStatus = lookupStatus
        };
    }

    [Fact]
    public void OnInit_RendersArtistImmediately_WithoutWaitingForEnrichment()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.NotAttempted)));

        // Never completes - if the page waited on this before rendering, the assertion below would time out.
        _client.EnrichArtist(5, Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<ArtistDetailsDto?>().Task);

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));

        cut.WaitForAssertion(() => cut.Find("h3").TextContent.ShouldContain("Tracks"));
    }

    [Fact]
    public void WhilePending_RendersHeaderWithNameAndReservedImageSlot()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.NotAttempted)));

        // Never completes - the header must already show the name and a reserved image slot
        // before enrichment resolves, so the layout doesn't jump once it does.
        _client.EnrichArtist(5, Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<ArtistDetailsDto?>().Task);

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".artist-header h2").TextContent.ShouldBe("Test Artist");
            cut.Find(".artist-header-image-pending").ShouldNotBeNull();
        });
    }

    [Fact]
    public void WhenEnrichmentCompletes_FillsInSummary()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.NotAttempted)));

        _client.EnrichArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.Found, "A great band.")));

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));

        cut.WaitForAssertion(() => cut.Find(".artist-header-summary").TextContent.ShouldContain("A great band."));
    }

    [Fact]
    public void WhenLookupAlreadyAttempted_DoesNotCallEnrichEndpoint()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.Found, "Already loaded.")));

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));

        cut.WaitForAssertion(() => cut.Find(".artist-header-summary").TextContent.ShouldContain("Already loaded."));

        _client.DidNotReceive().EnrichArtist(Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PopularTracks_ShowOnlyTheFirstFew_ButQueueThemAll()
    {
        // Six tracks clear the "popular" play-count bar, so the collapsed list shows three of them
        // and hides the rest behind "Show more".
        var artist = BuildArtist(5, ArtistDiscogsLookupStatus.Found, "Bio.");
        artist = new ArtistDetailsDto
        {
            Id = artist.Id,
            Name = artist.Name,
            Albums = [],
            DiscogsSummary = artist.DiscogsSummary,
            DiscogsLookupStatus = artist.DiscogsLookupStatus,
            Tracks = [.. Enumerable.Range(1, 6).Select(i => PopularTrack((uint)i, plays: (uint)(20 - i)))]
        };

        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(artist));

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));

        cut.WaitForAssertion(() => cut.FindComponents<Stub<MusicFileList>>().Count.ShouldBe(2));

        // First stub is the popular-tracks list, second is the full track list.
        var popular = cut.FindComponents<Stub<MusicFileList>>()[0];

        TracksOf(popular, nameof(MusicFileList.Music)).Count.ShouldBe(3);
        TracksOf(popular, nameof(MusicFileList.QueueMusic)).Select(t => t.Id).ShouldBe([1u, 2u, 3u, 4u, 5u, 6u]);
    }

    private static IReadOnlyCollection<TrackDto> TracksOf(IRenderedComponent<Stub<MusicFileList>> stub, string parameterName)
    {
        return (IReadOnlyCollection<TrackDto>)stub.Instance.Parameters[parameterName]!;
    }

    private static TrackDto PopularTrack(uint id, uint plays)
    {
        return new()
        {
            Id = id,
            Title = $"Track {id}",
            Artists = [],
            Album = "Test Album",
            AlbumId = 1,
            PlayCount = plays,
            TrackNumber = id,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    [Fact]
    public void WhenFavorited_ShowsFavoritedButton()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.Found, "Bio.")));
        _client.IsArtistFavorite(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));

        cut.WaitForAssertion(() => cut.Find(".action-btn").TextContent.ShouldContain("Favorited"));
    }

    [Fact]
    public void ClickingFavoriteButton_TogglesFavoriteState()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(BuildArtist(5, ArtistDiscogsLookupStatus.Found, "Bio.")));
        _client.IsArtistFavorite(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var cut = Render<Artist>(parameters => parameters.Add(p => p.ArtistId, 5));
        cut.WaitForAssertion(() => cut.Find(".action-btn").TextContent.ShouldContain("Favorite"));

        cut.Find(".action-btn").Click();

        cut.WaitForAssertion(() => cut.Find(".action-btn").TextContent.ShouldContain("Favorited"));

        _client.Received(1).SetArtistFavorite(5, true, Arg.Any<CancellationToken>());
    }
}
