using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class HomeTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task Home_ShowsRecentlyPlayedAndRecentlyAddedSections()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await Assertions.Expect(page.GetByText("Jump back in")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Recently added albums")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Recently added tracks")).ToBeVisibleAsync();

        await Assertions.Expect(page.GetByText(fixture.Catalog.TrackThree.Title).First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(fixture.Catalog.AlbumOne.Title).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Home_AlbumCard_NavigatesToAlbumPage()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GetByText(fixture.Catalog.AlbumOne.Title).First.ClickAsync();

        await page.WaitForURLAsync(url => url.Contains($"/albums/{fixture.Catalog.AlbumOne.Id}"));
    }
}
