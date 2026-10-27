using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class AlbumsAndArtistsTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task AlbumsPage_ListsSeededAlbums()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/albums");

        await Assertions.Expect(page.Locator(".album-link", new() { HasText = fixture.Catalog.AlbumOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = fixture.Catalog.AlbumTwo.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AlbumsPage_Search_FiltersList()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        await page.GotoAsync("/albums");

        await page.FillAsync(".search-input", fixture.Catalog.AlbumTwo.Title);

        await Assertions.Expect(page.Locator(".album-link", new() { HasText = fixture.Catalog.AlbumTwo.Title })).ToBeVisibleAsync();

        // CountAsync() is a one-shot check with no auto-retry, unlike Expect(...).ToBeVisibleAsync()
        // above - asserting "0" with it can race a still-in-flight Blazor Server re-render and read
        // stale DOM. Not.ToBeVisibleAsync() polls the same way the positive assertion does.
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = fixture.Catalog.AlbumOne.Title })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AlbumPage_PlayAll_StartsPlayback()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync($"/albums/{fixture.Catalog.AlbumOne.Id}");
        await Assertions.Expect(page.Locator("h2.album-title")).ToHaveTextAsync(fixture.Catalog.AlbumOne.Title);

        await page.Locator(".action-btn", new() { HasText = "Play" }).ClickAsync();

        await Assertions.Expect(page.Locator(".player-bar .track-title")).ToHaveTextAsync(fixture.Catalog.TrackOne.Title);
    }

    [Fact]
    public async Task AlbumPage_SeededFavoriteAlbum_ShowsAsFavoritedAndCanBeToggled()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync($"/albums/{fixture.Catalog.AlbumOne.Id}");

        var favoriteButton = page.Locator(".action-btn", new() { HasText = "Favorite" });
        await Assertions.Expect(favoriteButton).ToContainTextAsync("Favorited");

        await favoriteButton.ClickAsync();
        await Assertions.Expect(favoriteButton).Not.ToContainTextAsync("Favorited");

        await favoriteButton.ClickAsync();
        await Assertions.Expect(favoriteButton).ToContainTextAsync("Favorited");
    }

    [Fact]
    public async Task ArtistsPage_ListsSeededArtists()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/artists");

        await Assertions.Expect(page.Locator(".artist-link", new() { HasText = fixture.Catalog.ArtistOne.Name })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".artist-link", new() { HasText = fixture.Catalog.ArtistTwo.Name })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ArtistPage_ShowsAlbumsAndTracks()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync($"/Artists/{fixture.Catalog.ArtistOne.Id}");

        await Assertions.Expect(page.Locator("h2", new() { HasText = fixture.Catalog.ArtistOne.Name })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".album-card", new() { HasText = fixture.Catalog.AlbumOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ArtistsFavoritesFilter_OnlyShowsFavoritedArtist()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/artists");
        await page.Locator(".favorites-toggle-button").ClickAsync();

        await Assertions.Expect(page.Locator(".artist-link", new() { HasText = fixture.Catalog.ArtistTwo.Name })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".artist-link", new() { HasText = fixture.Catalog.ArtistOne.Name })).Not.ToBeVisibleAsync();
    }
}
