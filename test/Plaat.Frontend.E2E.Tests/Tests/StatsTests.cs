using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class StatsTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task StatsPage_ShowsSeededLibraryCounts()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/stats");

        await Assertions.Expect(page.Locator(".stat", new() { HasText = "Tracks" })).ToContainTextAsync("3");
        await Assertions.Expect(page.Locator(".stat", new() { HasText = "Albums" })).ToContainTextAsync("2");
        await Assertions.Expect(page.Locator(".stat", new() { HasText = "Artists" })).ToContainTextAsync("2");
    }

    [Fact]
    public async Task StatsPage_ShowsMostPlayedArtists()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/stats");

        await Assertions.Expect(page.Locator(".panel", new() { HasText = "Most played artists" })).ToContainTextAsync(fixture.Catalog.ArtistOne.Name);
    }
}
