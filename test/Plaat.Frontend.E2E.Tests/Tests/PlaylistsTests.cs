using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class PlaylistsTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task PlaylistsPage_ListsSeededPlaylistWithTrackCount()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/playlists");

        await Assertions.Expect(page.Locator(".album-link", new() { HasText = "Existing Playlist (2)" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OpeningPlaylist_ShowsItsTracks()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/playlists");
        await page.Locator(".album-link", new() { HasText = "Existing Playlist" }).ClickAsync();

        await Assertions.Expect(page.Locator("h2.playlist-title")).ToHaveTextAsync("Existing Playlist");
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreatingNewPlaylist_ThenAddingATrackToIt_Works()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var playlistName = $"E2E Playlist {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists");
        await page.ClickAsync("text=+ New Playlist");
        await page.FillAsync(".modal-input", playlistName);
        await page.Locator(".modal-btn-ok").ClickAsync();

        await Assertions.Expect(page.Locator(".album-link", new() { HasText = playlistName })).ToBeVisibleAsync();

        // The dedicated /tracks page has no per-row "More Actions" menu (see the equivalent comment
        // in TracksAndPlaybackTests) - only MusicFileList-based views do.
        await page.GotoAsync($"/albums/{fixture.Catalog.AlbumTwo.Id}");
        // The MudMenu "More Actions" activator is icon-only with no accessible name (its Label prop
        // isn't exposed as aria-label) - .mud-menu-icon-button-activator is the only reliable way to
        // find it.
        var row = page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title });
        await row.Locator(".mud-menu-icon-button-activator").ClickAsync();
        // The nested "Add to playlist" MudMenu opens its submenu on hover, not click.
        await page.GetByText("Add to playlist").HoverAsync();

        await page.GetByText(playlistName).ClickAsync();

        // AddToPlaylist's Api call happens entirely server-side over the existing Blazor Server
        // circuit (SignalR), not as a browser-visible network request - there is nothing client-side
        // to wait on (WaitForResponseAsync never sees it). ClickAsync only waits for the DOM click
        // event to dispatch, not for that server round-trip to finish, and hard-navigating away right
        // after (GotoAsync tears down the current circuit) risks cancelling it mid-flight. A short
        // fixed wait is the pragmatic fix here: the operation is a trivial local SQLite insert.
        await page.WaitForTimeoutAsync(1000);

        await page.GotoAsync($"/playlists/{Uri.EscapeDataString(playlistName)}");
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeletingPlaylist_RemovesItFromTheList()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var playlistName = $"E2E Playlist To Delete {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists");
        await page.ClickAsync("text=+ New Playlist");
        await page.FillAsync(".modal-input", playlistName);
        await page.Locator(".modal-btn-ok").ClickAsync();
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = playlistName })).ToBeVisibleAsync();

        var row = page.Locator(".playlist-row", new() { HasText = playlistName });
        await row.Locator(".delete-btn").ClickAsync();
        await page.Locator(".modal-btn-danger").ClickAsync();

        await Assertions.Expect(page.Locator(".album-link", new() { HasText = playlistName })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task DuplicatingPlaylist_FromItsMeatballMenu_CreatesACopyWithTheSameTracks()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var copyName = $"E2E Playlist Copy {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists/Existing%20Playlist");

        // Same icon-only MudMenu activator as the per-track menus (see the comment in
        // CreatingNewPlaylist_ThenAddingATrackToIt_Works) - it has no accessible name to target.
        await page.Locator(".playlist-header .mud-menu-icon-button-activator").ClickAsync();
        await page.GetByText("Duplicate playlist").ClickAsync();

        await page.FillAsync(".modal-input", copyName);
        await page.Locator(".modal-btn-ok").ClickAsync();

        // Duplicating navigates to the copy.
        await Assertions.Expect(page.Locator("h2.playlist-title")).ToHaveTextAsync(copyName);
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title })).ToBeVisibleAsync();

        // The original is untouched and both now show up in the listing.
        await page.GotoAsync("/playlists");
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = "Existing Playlist (2)" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = $"{copyName} (2)" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DuplicatingPlaylist_WithNameAlreadyInUse_ShowsAnError()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/playlists/Existing%20Playlist");

        await page.Locator(".playlist-header .mud-menu-icon-button-activator").ClickAsync();
        await page.GetByText("Duplicate playlist").ClickAsync();

        await page.FillAsync(".modal-input", "Existing Playlist");
        await page.Locator(".modal-btn-ok").ClickAsync();

        await Assertions.Expect(page.Locator(".modal-error")).ToHaveTextAsync("A playlist with this name already exists.");
    }
}
