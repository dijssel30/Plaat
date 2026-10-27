using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class PlaylistEditingTests(PlaatE2EFixture fixture)
{
    /// <summary>
    /// A private copy of the seeded two-track playlist, so a test can edit it freely.
    /// </summary>
    private static async Task<string> DuplicateExistingPlaylist(IPage page)
    {
        var copyName = $"E2E Editable {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists/Existing%20Playlist");
        await page.Locator(".playlist-header .mud-menu-icon-button-activator").ClickAsync();
        await page.GetByText("Duplicate playlist").ClickAsync();
        await page.FillAsync(".modal-input", copyName);
        await page.Locator(".modal-btn-ok").ClickAsync();
        await Assertions.Expect(page.Locator("h2.playlist-title")).ToHaveTextAsync(copyName);

        return copyName;
    }

    [Fact]
    public async Task PinningAPlaylist_AddsItToTheSidebarAndUnpinningRemovesIt()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var name = $"E2E Pinned {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists");
        await page.ClickAsync("text=+ New Playlist");
        await page.FillAsync(".modal-input", name);
        await page.Locator(".modal-btn-ok").ClickAsync();
        var row = page.Locator(".playlist-row", new() { HasText = name });
        var sidebarLink = page.Locator(".playlist-link", new() { HasText = name });
        await Assertions.Expect(sidebarLink).Not.ToBeVisibleAsync();

        await row.Locator(".pin-btn").ClickAsync();

        await Assertions.Expect(row.Locator(".pin-btn.active")).ToBeVisibleAsync();
        await Assertions.Expect(sidebarLink).ToBeVisibleAsync();

        // The pin is stored server-side, so it survives a full reload.
        await page.ReloadAsync();
        await Assertions.Expect(sidebarLink).ToBeVisibleAsync();

        await page.Locator(".playlist-row", new() { HasText = name }).Locator(".pin-btn").ClickAsync();

        await Assertions.Expect(sidebarLink).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task RemovingATrackInEditMode_TakesItOutOfThePlaylist()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var name = await DuplicateExistingPlaylist(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        await page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title }).Locator(".remove-button").ClickAsync();

        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("h3", new() { HasText = "Tracks (1)" })).ToBeVisibleAsync();

        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("h2.playlist-title")).ToHaveTextAsync(name);
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayingAPlaylist_StartsItsFirstTrack()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/playlists/Existing%20Playlist");
        await page.Locator(".action-btn", new() { HasTextString = "Play" }).First.ClickAsync();

        await Assertions.Expect(page.Locator(".track-table-row.active", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UnknownPlaylist_ShowsNotFound()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync($"/playlists/{Guid.NewGuid():N}");

        await Assertions.Expect(page.Locator("h3", new() { HasText = "Playlist not found" })).ToBeVisibleAsync();
    }
}
