using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class PlaylistRepositoryTests
{
    [Fact]
    public async Task CreatePlaylist_TwoUsers_CanUseSameName()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Favorites", alice.Id, [], CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            repo.CreatePlaylist("Favorites", bob.Id, [], CancellationToken.None));

        exception.ShouldBeNull();
    }

    [Fact]
    public async Task CreatePlaylist_SameUserSameName_Throws()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Favorites", alice.Id, [], CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            repo.CreatePlaylist("Favorites", alice.Id, [], CancellationToken.None));

        exception.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAllPlaylists_OnlyReturnsCallingUsersPlaylists()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        await repo.CreatePlaylist("Bob's list", bob.Id, [], CancellationToken.None);

        var aliceResult = await repo.GetAllPlaylists(alice.Id, CancellationToken.None);

        aliceResult.ShouldHaveSingleItem();
        aliceResult.Single().PlaylistName.ShouldBe("Alice's list");
    }

    [Fact]
    public async Task GetPlaylist_ForAnotherUsersPlaylist_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);

        var result = await repo.GetPlaylist("Alice's list", bob.Id, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPlaylist_ForUnknownName_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();

        var result = await repo.GetPlaylist("Does not exist", alice.Id, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPlaylist_ForOwnPlaylist_ReturnsIt()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);

        var result = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.PlaylistName.ShouldBe("Alice's list");
    }

    [Fact]
    public async Task AddToPlaylist_ForAnotherUsersPlaylist_ThrowsAndDoesNotModifyIt()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var exception = await Record.ExceptionAsync(() =>
            repo.AddToPlaylist(playlistId, bob.Id, [track.Id], CancellationToken.None));

        exception.ShouldNotBeNull();
        var aliceView = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);
        aliceView.ShouldNotBeNull();
        aliceView.Tracks.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReorderPlaylist_WithFullMatchingTrackSet_ReordersAndReturnsTrue()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var trackA = fixture.SeedTrack(album, "A", "/music/reorder-a.mp3");
        var trackB = fixture.SeedTrack(album, "B", "/music/reorder-b.mp3");
        var trackC = fixture.SeedTrack(album, "C", "/music/reorder-c.mp3");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [trackA.Id, trackB.Id, trackC.Id], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var success = await repo.ReorderPlaylist(
            playlistId, alice.Id, [trackC.Id, trackA.Id, trackB.Id], CancellationToken.None);

        success.ShouldBeTrue();
        var result = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([trackC.Id, trackA.Id, trackB.Id]);
    }

    [Fact]
    public async Task ReorderPlaylist_MissingATrackId_ReturnsFalseAndDoesNotModifyOrder()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var trackA = fixture.SeedTrack(album, "A", "/music/missing-a.mp3");
        var trackB = fixture.SeedTrack(album, "B", "/music/missing-b.mp3");
        var trackC = fixture.SeedTrack(album, "C", "/music/missing-c.mp3");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [trackA.Id, trackB.Id, trackC.Id], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var success = await repo.ReorderPlaylist(
            playlistId, alice.Id, [trackC.Id, trackA.Id], CancellationToken.None);

        success.ShouldBeFalse();
        var result = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([trackA.Id, trackB.Id, trackC.Id]);
    }

    [Fact]
    public async Task ReorderPlaylist_WithDuplicateTrackId_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var trackA = fixture.SeedTrack(album, "A", "/music/dup-a.mp3");
        var trackB = fixture.SeedTrack(album, "B", "/music/dup-b.mp3");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [trackA.Id, trackB.Id], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var success = await repo.ReorderPlaylist(
            playlistId, alice.Id, [trackA.Id, trackA.Id], CancellationToken.None);

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task ReorderPlaylist_ForAnotherUsersPlaylist_ReturnsFalseAndDoesNotModifyIt()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");
        var album = fixture.SeedAlbum("Test Album");
        var trackA = fixture.SeedTrack(album, "A", "/music/otheruser-a.mp3");
        var trackB = fixture.SeedTrack(album, "B", "/music/otheruser-b.mp3");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [trackA.Id, trackB.Id], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var success = await repo.ReorderPlaylist(
            playlistId, bob.Id, [trackB.Id, trackA.Id], CancellationToken.None);

        success.ShouldBeFalse();
        var result = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([trackA.Id, trackB.Id]);
    }

    [Fact]
    public async Task RemovePlaylist_ForAnotherUsersPlaylist_DoesNotDeleteIt()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        await repo.RemovePlaylist(playlistId, false, bob.Id, CancellationToken.None);

        var stillThere = await repo.GetAllPlaylists(alice.Id, CancellationToken.None);
        stillThere.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RemovePlaylist_ForOwnPlaylist_DeletesIt()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        await repo.RemovePlaylist(playlistId, false, alice.Id, CancellationToken.None);

        var result = await repo.GetAllPlaylists(alice.Id, CancellationToken.None);
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task DuplicatePlaylist_ForOwnPlaylist_CopiesTracksInOrderAndLeavesOriginal()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var trackA = fixture.SeedTrack(album, "A", "/music/duplicate-a.mp3");
        var trackB = fixture.SeedTrack(album, "B", "/music/duplicate-b.mp3");
        var trackC = fixture.SeedTrack(album, "C", "/music/duplicate-c.mp3");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [trackA.Id, trackB.Id, trackC.Id], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;
        await repo.ReorderPlaylist(playlistId, alice.Id, [trackC.Id, trackA.Id, trackB.Id], CancellationToken.None);

        var duplicated = await repo.DuplicatePlaylist(
            playlistId, false, alice.Id, "Alice's list (copy)", CancellationToken.None);

        duplicated.ShouldBeTrue();
        var copy = await repo.GetPlaylist("Alice's list (copy)", alice.Id, CancellationToken.None);
        copy.ShouldNotBeNull();
        copy.Tracks.Select(t => t.Id).ShouldBe([trackC.Id, trackA.Id, trackB.Id]);

        var original = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);
        original.ShouldNotBeNull();
        original.Tracks.Select(t => t.Id).ShouldBe([trackC.Id, trackA.Id, trackB.Id]);
    }

    [Fact]
    public async Task DuplicatePlaylist_CopyIsIndependentOfTheOriginal()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var trackA = fixture.SeedTrack(album, "A", "/music/independent-a.mp3");
        var trackB = fixture.SeedTrack(album, "B", "/music/independent-b.mp3");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [trackA.Id, trackB.Id], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        await repo.DuplicatePlaylist(playlistId, false, alice.Id, "Copy", CancellationToken.None);
        var copyId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None))
            .Single(pl => pl.PlaylistName == "Copy").Id;

        await repo.RemoveFromPlaylist(copyId, alice.Id, trackA.Id, CancellationToken.None);

        var original = await repo.GetPlaylist("Alice's list", alice.Id, CancellationToken.None);
        original.ShouldNotBeNull();
        original.Tracks.Select(t => t.Id).ShouldBe([trackA.Id, trackB.Id]);
    }

    [Fact]
    public async Task DuplicatePlaylist_DoesNotCopyPinnedState()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;
        await repo.SetPlaylistPinned(playlistId, false, alice.Id, true, CancellationToken.None);

        await repo.DuplicatePlaylist(playlistId, false, alice.Id, "Copy", CancellationToken.None);

        var copy = await repo.GetPlaylist("Copy", alice.Id, CancellationToken.None);
        copy.ShouldNotBeNull();
        copy.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task DuplicatePlaylist_WithNameAlreadyInUse_Throws()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        await repo.CreatePlaylist("Taken", alice.Id, [], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None))
            .Single(pl => pl.PlaylistName == "Alice's list").Id;

        await Should.ThrowAsync<System.Data.DuplicateNameException>(() =>
            repo.DuplicatePlaylist(playlistId, false, alice.Id, "taken", CancellationToken.None));
    }

    [Fact]
    public async Task DuplicatePlaylist_ForAnotherUsersPlaylist_ReturnsFalseAndCopiesNothing()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Alice's list", alice.Id, [], CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var duplicated = await repo.DuplicatePlaylist(
            playlistId, false, bob.Id, "Stolen", CancellationToken.None);

        duplicated.ShouldBeFalse();
        (await repo.GetAllPlaylists(bob.Id, CancellationToken.None)).ShouldBeEmpty();
    }
}
