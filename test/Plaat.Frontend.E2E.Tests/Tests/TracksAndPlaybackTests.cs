using Microsoft.Playwright;
using Xunit;

namespace Plaat.Frontend.E2E.Tests.Tests;

[Collection(PlaatE2ECollection.Name)]
public sealed class TracksAndPlaybackTests(PlaatE2EFixture fixture)
{
    [Fact]
    public async Task TracksPage_ListsSeededTracks()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();

        await page.GotoAsync("/tracks");

        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ClickingTrackRow_StartsPlaybackAndStreamsRealAudio()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        await page.GotoAsync("/tracks");

        await page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title }).ClickAsync();

        var playerBar = page.Locator(".player-bar");
        await Assertions.Expect(playerBar.Locator(".track-title")).ToHaveTextAsync(fixture.Catalog.TrackOne.Title);

        var audio = playerBar.Locator("audio");
        await PollAsync.Until(
            () => audio.EvaluateAsync<double>("el => el.duration || 0"),
            duration => duration > 0,
            "the <audio> element to report a real duration once the seeded WAV file loads");
    }

    [Fact]
    public async Task PlayerControls_PauseAndResume_ToggleAudioPausedState()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        await page.GotoAsync("/tracks");
        await page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackOne.Title }).ClickAsync();

        var audio = page.Locator(".player-bar audio");
        await PollAsync.Until(() => audio.EvaluateAsync<bool>("el => !el.paused"), playing => playing, "audio to start playing");

        await page.ClickAsync("button[title='Pause']");
        await PollAsync.Until(() => audio.EvaluateAsync<bool>("el => el.paused"), paused => paused, "audio to pause");

        await page.ClickAsync("button[title='Play']");
        await PollAsync.Until(() => audio.EvaluateAsync<bool>("el => !el.paused"), playing => playing, "audio to resume");
    }

    [Fact]
    public async Task PlayerControls_Next_AdvancesToNextTrackInQueue()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        await page.GotoAsync($"/albums/{fixture.Catalog.AlbumOne.Id}");
        await page.Locator(".action-btn", new() { HasText = "Play" }).ClickAsync();

        await Assertions.Expect(page.Locator(".player-bar .track-title")).ToHaveTextAsync(fixture.Catalog.TrackOne.Title);

        await page.ClickAsync("button[title='Next']");

        await Assertions.Expect(page.Locator(".player-bar .track-title")).ToHaveTextAsync(fixture.Catalog.TrackTwo.Title);
    }

    [Fact]
    public async Task RatingTrackFromPlayerBar_PersistsAndReflectsAsCleared()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        await page.GotoAsync("/tracks");
        await page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title }).ClickAsync();

        var fiveStars = page.Locator(".player-bar .star-rating button[aria-label='Rate 5 stars']");
        await fiveStars.ClickAsync();

        await Assertions.Expect(page.Locator(".player-bar .star-rating button[aria-label='Clear rating']")).ToBeVisibleAsync();

        await page.ReloadAsync();
        await page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackTwo.Title }).ClickAsync();
        await Assertions.Expect(page.Locator(".player-bar .star-rating button[aria-label='Clear rating']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddToQueue_FromTrackMenu_AddsTrackToQueuePage()
    {
        await using var page = await fixture.NewAuthenticatedPageAsync();
        // The dedicated /tracks page is a separate, leaner implementation (server-paginated
        // Virtualize) with no per-row "More Actions" menu - only MusicFileList-based views (album,
        // artist, search, home, playlist) have it, so this needs to start from one of those instead.
        await page.GotoAsync($"/albums/{fixture.Catalog.AlbumTwo.Id}");

        // The MudMenu "More Actions" activator is icon-only with no accessible name (its Label prop
        // isn't exposed as aria-label) - .mud-menu-icon-button-activator is the only reliable way to
        // find it.
        var row = page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title });
        await row.Locator(".mud-menu-icon-button-activator").ClickAsync();
        await page.GetByText("Add to queue", new() { Exact = true }).ClickAsync();

        await page.GotoAsync("/queue");
        await Assertions.Expect(page.Locator(".track-table-row", new() { HasText = fixture.Catalog.TrackThree.Title })).ToBeVisibleAsync();
    }
}
