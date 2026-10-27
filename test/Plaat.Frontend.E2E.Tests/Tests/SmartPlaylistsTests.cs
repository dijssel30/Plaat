using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class SmartPlaylistsTests(PlaatE2EFixture fixture)
{
    private static async Task SetGenreRule(IPage page, string genre)
    {
        var rule = page.Locator(".rule-row").First;
        await rule.Locator(".rule-select").First.SelectOptionAsync("Genre");
        await rule.Locator(".rule-select-op").SelectOptionAsync("Equals");
        await rule.Locator("input[placeholder='value']").FillAsync(genre);
    }

    [Fact]
    public async Task CreatingASmartPlaylist_ShowsOnlyTracksMatchingItsRules()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var name = $"E2E Smart {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists");
        await page.ClickAsync("text=+ New Smart Playlist");
        await page.FillAsync(".modal-input", name);
        await SetGenreRule(page, "Rock");
        await page.Locator(".modal-btn-ok").ClickAsync();

        // Both seeded Rock tracks match; the Jazz one doesn't.
        await Assertions.Expect(page.Locator(".album-link", new() { HasText = $"{name} (2)" })).ToBeVisibleAsync();

        await page.Locator(".album-link", new() { HasText = name }).ClickAsync();

        await Assertions.Expect(page.Locator("h2.playlist-title")).ToHaveTextAsync(name);
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title })).Not.ToBeVisibleAsync();

        // Smart playlists are computed, so there is no manual "Edit" mode for their track list.
        await Assertions.Expect(page.Locator(".action-btn", new() { HasTextString = "Edit Rules" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditingASmartPlaylistsRules_ChangesWhichTracksItHolds()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        var name = $"E2E Smart Edit {Guid.NewGuid():N}";

        await page.GotoAsync("/playlists");
        await page.ClickAsync("text=+ New Smart Playlist");
        await page.FillAsync(".modal-input", name);
        await SetGenreRule(page, "Rock");
        await page.Locator(".modal-btn-ok").ClickAsync();
        await page.Locator(".album-link", new() { HasText = name }).ClickAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();

        await page.Locator(".action-btn", new() { HasTextString = "Edit Rules" }).ClickAsync();
        await Assertions.Expect(page.Locator(".rule-row input[placeholder='value']")).ToHaveValueAsync("Rock");
        await page.Locator(".rule-row input[placeholder='value']").FillAsync("Jazz");
        await page.Locator(".modal-btn-ok").ClickAsync();

        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).Not.ToBeVisibleAsync();

        // The new rules were persisted, not just applied to this page.
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreatingASmartPlaylistWithoutRules_IsRejected()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/playlists");
        await page.ClickAsync("text=+ New Smart Playlist");
        await page.FillAsync(".modal-input", $"E2E Empty Smart {Guid.NewGuid():N}");
        await page.Locator(".rule-row .remove-rule-btn").ClickAsync();
        await page.Locator(".modal-btn-ok").ClickAsync();

        await Assertions.Expect(page.Locator(".modal-error")).ToHaveTextAsync("Add at least one rule.");
    }
}
