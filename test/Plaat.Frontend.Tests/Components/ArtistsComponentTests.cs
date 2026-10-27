using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Pages;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class ArtistsComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public ArtistsComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
    }

    [Fact]
    public void OnInit_LoadsArtistsFromClient()
    {
        _client.GetArtists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ArtistOnlyDto>>([
                new ArtistOnlyDto { Id = 1, Name = "Artist One" },
                new ArtistOnlyDto { Id = 2, Name = "Artist Two" }
            ]));

        var cut = Render<Artists>();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".artist-link-text");
            links.Count.ShouldBe(2);
        });
    }

    [Fact]
    public void OnInit_SortsArtistsByName()
    {
        _client.GetArtists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ArtistOnlyDto>>([
                new ArtistOnlyDto { Id = 2, Name = "Zappa" },
                new ArtistOnlyDto { Id = 1, Name = "Adams" }
            ]));

        var cut = Render<Artists>();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".artist-link-text");
            links[0].TextContent.ShouldBe("Adams");
            links[1].TextContent.ShouldBe("Zappa");
        });
    }

    [Fact]
    public void OnInit_WhenEmpty_RendersNoArtistLinks()
    {
        _client.GetArtists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ArtistOnlyDto>>([]));

        var cut = Render<Artists>();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".artist-link-text");
            links.ShouldBeEmpty();
        });
    }

    [Fact]
    public void OnInit_ShowsArtistName()
    {
        _client.GetArtists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ArtistOnlyDto>>([
                new ArtistOnlyDto { Id = 1, Name = "Pink Floyd" }
            ]));

        var cut = Render<Artists>();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find(".artist-link-text");
            link.TextContent.ShouldBe("Pink Floyd");
        });
    }

    [Fact]
    public void ClickingFavoritesToggle_SwitchesToFavoriteArtistsOnly()
    {
        _client.GetArtists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ArtistOnlyDto>>([
                new ArtistOnlyDto { Id = 1, Name = "All Artists Only" }
            ]));
        _client.GetFavoriteArtists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ArtistOnlyDto>>([
                new ArtistOnlyDto { Id = 2, Name = "Favorite Artist" }
            ]));

        var cut = Render<Artists>();
        cut.WaitForAssertion(() => cut.Find(".artist-link-text").TextContent.ShouldBe("All Artists Only"));

        cut.Find(".favorites-toggle-button").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".artist-link-text").TextContent.ShouldBe("Favorite Artist");
            cut.Find("h3").TextContent.ShouldContain("Favorite Artists");
        });
    }
}
