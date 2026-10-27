using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class TrackInfoTests(PlaatE2EFixture fixture)
{
    private async Task OpenTrackInfo(IPage page, string trackTitle, uint albumId)
    {
        await page.GotoAsync($"/albums/{albumId}");
        var row = page.Locator(".track-table-row", new() { HasText = trackTitle });
        await row.Locator(".mud-menu-icon-button-activator").ClickAsync();
        await page.GetByText("Track info").ClickAsync();
        await Assertions.Expect(page.Locator(".modal-title", new() { HasText = "Track Info" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TrackInfo_ShowsTheTracksDetailsAndLocations()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var track = fixture.Catalog.TrackTwo;

        await OpenTrackInfo(page, track.Title, fixture.Catalog.AlbumOne.Id);

        await Assertions.Expect(page.Locator(".modal-body h4")).ToHaveTextAsync(track.Title);
        await Assertions.Expect(page.Locator(".modal-body .mono", new() { HasText = track.FileHash })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".modal-body td.mono", new() { HasText = track.TrackLocations.First().Path })).ToBeVisibleAsync();

        // The seeded plays (one an hour ago, one two days ago) show up as a chart, not the empty state.
        await Assertions.Expect(page.Locator(".modal-body", new() { HasText = "No plays recorded yet." })).Not.ToBeVisibleAsync();

        await page.Locator(".modal-footer button", new() { HasText = "Close" }).ClickAsync();
        await Assertions.Expect(page.Locator(".modal-title", new() { HasText = "Track Info" })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task TrackInfo_AdminTrimPoints_AreSavedAndShownAgain()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var track = fixture.Catalog.TrackThree;

        await OpenTrackInfo(page, track.Title, fixture.Catalog.AlbumTwo.Id);
        var start = page.Locator("input[placeholder='mm:ss']").First;
        await start.FillAsync("0:01");
        await start.PressAsync("Tab");
        await page.Locator(".modal-body button", new() { HasText = "Save" }).ClickAsync();
        await Assertions.Expect(start).ToHaveValueAsync("00:01");

        await page.ReloadAsync();
        await OpenTrackInfo(page, track.Title, fixture.Catalog.AlbumTwo.Id);
        await Assertions.Expect(page.Locator("input[placeholder='mm:ss']").First).ToHaveValueAsync("00:01");

        // Clearing removes them again, so the shared seeded track is left as it was.
        await page.Locator(".modal-body button", new() { HasText = "Clear" }).ClickAsync();
        await Assertions.Expect(page.Locator("input[placeholder='mm:ss']").First).ToHaveValueAsync("");
    }

    [Fact]
    public async Task TrackInfo_InvalidTrimInput_IsRejected()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await OpenTrackInfo(page, fixture.Catalog.TrackOne.Title, fixture.Catalog.AlbumOne.Id);
        var start = page.Locator("input[placeholder='mm:ss']").First;
        await start.FillAsync("abc");
        await start.PressAsync("Tab");
        await page.Locator(".modal-body button", new() { HasText = "Save" }).ClickAsync();

        await Assertions.Expect(page.Locator(".modal-body p.text-danger")).ToHaveTextAsync("Enter times as mm:ss.");
    }

    [Fact]
    public async Task TrackInfo_NonAdmin_DoesNotSeeTrimEditing()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync("secondary", SeededCatalog.SecondaryUserPassword);

        await OpenTrackInfo(page, fixture.Catalog.TrackOne.Title, fixture.Catalog.AlbumOne.Id);

        await Assertions.Expect(page.Locator(".modal-body h4")).ToHaveTextAsync(fixture.Catalog.TrackOne.Title);
        await Assertions.Expect(page.Locator("input[placeholder='mm:ss']")).ToHaveCountAsync(0);
    }
}
