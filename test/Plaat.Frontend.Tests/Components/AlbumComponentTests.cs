using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
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

public sealed class AlbumComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public AlbumComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new MusicPlaylist());
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
        Services.AddMudServices();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Unauthenticated, so the admin-only cover-upload menu (the page's only MudBlazor markup)
        // stays out of the render.
        AddAuthorization();

        ComponentFactories.AddStub<MusicFileList>();
    }

    [Fact]
    public void WhenTracksShareAYear_ShowsThatYear()
    {
        SetUpAlbum(Track(1, disc: 1, number: 1, year: 1994), Track(2, disc: 1, number: 2, year: 1994));

        var cut = Render<Album>(p => p.Add(x => x.AlbumId, 7));

        cut.WaitForAssertion(() => cut.Find(".album-year").TextContent.Trim().ShouldBe("1994"));
    }

    [Fact]
    public void WhenTracksSpanYears_ShowsARange()
    {
        SetUpAlbum(Track(1, disc: 1, number: 1, year: 1994), Track(2, disc: 1, number: 2, year: 1997));

        var cut = Render<Album>(p => p.Add(x => x.AlbumId, 7));

        cut.WaitForAssertion(() => cut.Find(".album-year").TextContent.Trim().ShouldBe("1994–1997"));
    }

    [Fact]
    public void WhenNoTrackHasAYear_ShowsNoYear()
    {
        SetUpAlbum(Track(1, disc: 1, number: 1, year: 0));

        var cut = Render<Album>(p => p.Add(x => x.AlbumId, 7));

        cut.WaitForAssertion(() => cut.Find(".album-title").ShouldNotBeNull());
        cut.FindAll(".album-year").ShouldBeEmpty();
    }

    [Fact]
    public void WhenSingleDisc_RendersOneUnheadedTrackList()
    {
        SetUpAlbum(Track(1, disc: 1, number: 1), Track(2, disc: 1, number: 2));

        var cut = Render<Album>(p => p.Add(x => x.AlbumId, 7));

        cut.WaitForAssertion(() => cut.FindComponents<Stub<MusicFileList>>().Count.ShouldBe(1));
        cut.FindAll(".disc-header").ShouldBeEmpty();
    }

    [Fact]
    public void WhenMultiDisc_RendersAHeadedTrackListPerDisc()
    {
        SetUpAlbum(
            Track(1, disc: 1, number: 1),
            Track(2, disc: 2, number: 1),
            Track(3, disc: 2, number: 2));

        var cut = Render<Album>(p => p.Add(x => x.AlbumId, 7));

        cut.WaitForAssertion(() =>
        {
            var headers = cut.FindAll(".disc-header");
            headers.Count.ShouldBe(2);
            headers[0].TextContent.ShouldBe("CD 1");
            headers[1].TextContent.ShouldBe("CD 2");
        });

        var lists = cut.FindComponents<Stub<MusicFileList>>();
        lists.Count.ShouldBe(2);
        TracksOf(lists[0], nameof(MusicFileList.Music)).Select(t => t.Id).ShouldBe([1u]);
        TracksOf(lists[1], nameof(MusicFileList.Music)).Select(t => t.Id).ShouldBe([2u, 3u]);
    }

    [Fact]
    public void WhenMultiDisc_EachDiscQueuesTheWholeAlbum()
    {
        SetUpAlbum(
            Track(1, disc: 1, number: 1),
            Track(2, disc: 2, number: 1));

        var cut = Render<Album>(p => p.Add(x => x.AlbumId, 7));

        cut.WaitForAssertion(() => cut.FindComponents<Stub<MusicFileList>>().Count.ShouldBe(2));

        foreach (var list in cut.FindComponents<Stub<MusicFileList>>())
        {
            TracksOf(list, nameof(MusicFileList.QueueMusic)).Select(t => t.Id).ShouldBe([1u, 2u]);
        }
    }

    private void SetUpAlbum(params TrackDto[] tracks)
    {
        _client.GetAlbum(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AlbumDetailsDto?>(new AlbumDetailsDto
            {
                Id = 7,
                Title = "Test Album",
                Artists = [new ArtistOnlyDto { Id = 1, Name = "Test Artist" }],
                Tracks = tracks
            }));
    }

    private static IReadOnlyCollection<TrackDto> TracksOf(IRenderedComponent<Stub<MusicFileList>> stub, string parameterName)
    {
        return (IReadOnlyCollection<TrackDto>)stub.Instance.Parameters[parameterName]!;
    }

    private static TrackDto Track(uint id, uint disc, uint number, uint year = 2000)
    {
        return new()
        {
            Id = id,
            Title = $"Track {id}",
            Artists = [],
            Album = "Test Album",
            AlbumId = 7,
            PlayCount = 0,
            TrackNumber = number,
            DiscNumber = disc,
            Year = year,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
