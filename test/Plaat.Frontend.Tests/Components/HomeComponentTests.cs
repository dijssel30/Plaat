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

public sealed class HomeComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public HomeComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        _client.GetRecentlyPlayed(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TrackDto>>([]));
        _client.GetRecentlyAdded(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecentlyAddedDto()));

        Services.AddSingleton(_client);
        Services.AddSingleton(new MusicPlaylist());
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });

        // Stub MusicFileList to avoid needing MudBlazor services
        ComponentFactories.AddStub<MusicFileList>();
    }

    [Fact]
    public void OnInit_FetchesRecentlyPlayedAndAdded()
    {
        _client.GetRecentlyPlayed(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TrackDto>>([MakeTrack(1, "Song A")]));

        Render<Home>();

        _client.Received().GetRecentlyPlayed(Arg.Any<CancellationToken>());
        _client.Received().GetRecentlyAdded(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OnInit_WhenRecentlyPlayedHasTracks_RendersTrackList()
    {
        _client.GetRecentlyPlayed(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TrackDto>>([MakeTrack(1, "Song A"), MakeTrack(2, "Song B")]));

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
        {
            var stub = cut.FindComponent<Stub<MusicFileList>>();
            stub.ShouldNotBeNull();
        });
    }

    [Fact]
    public void OnInit_WhenEverythingEmpty_RendersEmptyState()
    {
        var cut = Render<Home>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Browse all tracks"));
    }

    private static TrackDto MakeTrack(uint id, string title)
    {
        return new()
        {
            Id = id,
            Title = title,
            Artists = [],
            Album = "Test Album",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = id,
            DiscNumber = 1,
            Year = 2024,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
