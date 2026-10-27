using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class SearchTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task SearchingArtistName_FindsArtistAlbumAndTrack()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/search");
        await page.FillAsync(".search-input", "Test Artist One");

        await Assertions.Expect(page.Locator(".artist-link", new() { HasText = fixture.Catalog.ArtistOne.Name })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = fixture.Catalog.AlbumOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SearchingForNonExistentText_ShowsEmptyState()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/search");
        await page.FillAsync(".search-input", "definitely-not-in-the-library-xyz");

        await Assertions.Expect(page.Locator(".search-empty").First).ToBeVisibleAsync();
    }
}
