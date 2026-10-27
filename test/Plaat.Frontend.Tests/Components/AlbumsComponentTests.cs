using Bunit;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Pages;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class AlbumsComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public AlbumsComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
        Services.AddDataProtection();
        Services.AddScoped<ProtectedLocalStorage>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void OnInit_WithNoArtistId_LoadsAllAlbumsFromClient()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 1, Title = "Album One", Artists = [new ArtistOnlyDto { Name = "Artist A" }] },
                new AlbumOnlyDto { Id = 2, Title = "Album Two", Artists = [new ArtistOnlyDto { Name = "Artist B" }] }
            ]));

        var cut = Render<Albums>();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".album-link-text");
            links.Count.ShouldBe(2);
        });
    }

    [Fact]
    public void OnInit_WithNoArtistId_SortsAlbumsByTitle()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 2, Title = "Zephyr", Artists = [new ArtistOnlyDto { Name = "Artist A" }] },
                new AlbumOnlyDto { Id = 1, Title = "Apollo", Artists = [new ArtistOnlyDto { Name = "Artist B" }] }
            ]));

        var cut = Render<Albums>();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".album-link-text");
            links[0].TextContent.ShouldContain("Apollo");
            links[1].TextContent.ShouldContain("Zephyr");
        });
    }

    [Fact]
    public void OnInit_WithArtistId_LoadsArtistAlbums()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(new ArtistDetailsDto
            {
                Id = 5,
                Name = "Test Artist",
                Tracks = [],
                Albums = [new AlbumOnlyDto { Id = 10, Title = "Artist Album", Artists = [new ArtistOnlyDto { Name = "Test Artist" }] }],
                DiscogsLookupStatus = ArtistDiscogsLookupStatus.NotAttempted
            }));

        var cut = Render<Albums>(p => p.Add(x => x.ArtistId, 5));

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".album-link-text");
            links.ShouldHaveSingleItem();
            links[0].TextContent.ShouldContain("Artist Album");
        });
    }

    [Fact]
    public void OnInit_WithNoArtistId_ShowsAlbumWithArtistAndTitle()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 1, Title = "My Album", Artists = [new ArtistOnlyDto { Name = "My Artist" }] }
            ]));

        var cut = Render<Albums>();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find(".album-link-text");
            link.TextContent.ShouldContain("My Artist");
            link.TextContent.ShouldContain("My Album");
        });
    }

    [Fact]
    public void DefaultsToListView()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 1, Title = "My Album", Artists = [new ArtistOnlyDto { Name = "My Artist" }] }
            ]));

        var cut = Render<Albums>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".album-list-container").ShouldHaveSingleItem();
            cut.FindAll(".album-grid-container").ShouldBeEmpty();
        });
    }

    [Fact]
    public void ClickingGridToggle_SwitchesToGridViewWithCoverImages()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 1, Title = "My Album", Artists = [new ArtistOnlyDto { Name = "My Artist" }] }
            ]));

        var cut = Render<Albums>();
        cut.WaitForAssertion(() => cut.FindAll(".album-list-container").ShouldHaveSingleItem());

        cut.Find(".view-toggle-button:last-child").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".album-list-container").ShouldBeEmpty();
            var card = cut.Find(".album-grid-card");
            card.QuerySelector("img")!.GetAttribute("src").ShouldBe("/proxy/cover/album/1");
        });
    }

    [Fact]
    public void ClickingFavoritesToggle_SwitchesToFavoriteAlbumsOnly()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 1, Title = "All Albums Only", Artists = [new ArtistOnlyDto { Name = "Artist A" }] }
            ]));
        _client.GetFavoriteAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 2, Title = "Favorite Album", Artists = [new ArtistOnlyDto { Name = "Artist B" }] }
            ]));

        var cut = Render<Albums>();
        cut.WaitForAssertion(() => cut.Find(".album-link-text").TextContent.ShouldContain("All Albums Only"));

        cut.Find(".favorites-toggle-button").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".album-link-text").TextContent.ShouldContain("Favorite Album");
            cut.Find("h3").TextContent.ShouldContain("Favorite Albums");
        });
    }

    [Fact]
    public void WithArtistId_DoesNotShowFavoritesToggle()
    {
        _client.GetArtist(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ArtistDetailsDto?>(new ArtistDetailsDto
            {
                Id = 5,
                Name = "Test Artist",
                Tracks = [],
                Albums = [],
                DiscogsLookupStatus = ArtistDiscogsLookupStatus.NotAttempted
            }));

        var cut = Render<Albums>(p => p.Add(x => x.ArtistId, 5));

        cut.WaitForAssertion(() => cut.FindAll(".favorites-toggle-button").ShouldBeEmpty());
    }

    [Fact]
    public void MovingCoverSizeSlider_UpdatesGridCoverSizeVariable()
    {
        _client.GetAlbums(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<AlbumOnlyDto>>([
                new AlbumOnlyDto { Id = 1, Title = "My Album", Artists = [new ArtistOnlyDto { Name = "My Artist" }] }
            ]));

        var cut = Render<Albums>();
        cut.WaitForAssertion(() => cut.FindAll(".album-list-container").ShouldHaveSingleItem());

        cut.Find(".view-toggle-button:last-child").Click();
        cut.WaitForAssertion(() => cut.FindAll(".cover-size-slider").ShouldHaveSingleItem());

        cut.Find(".cover-size-slider").Input("250");

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find(".album-grid-list").GetAttribute("style");
            style.ShouldNotBeNull();
            style.ShouldContain("--cover-size: 250px");
        });
    }
}
