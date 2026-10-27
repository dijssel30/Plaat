using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class LibrarySearchServiceTests
{
    private readonly ITrackRepository _tracks = Substitute.For<ITrackRepository>();
    private readonly IAlbumRepository _albums = Substitute.For<IAlbumRepository>();
    private readonly IArtistRepository _artists = Substitute.For<IArtistRepository>();

    private LibrarySearchService CreateSut()
    {
        return new(_tracks, _albums, _artists);
    }

    [Fact]
    public async Task Search_MatchesDiacriticInsensitively_AcrossTitleArtistAlbum()
    {
        _tracks.GetAll(Arg.Any<CancellationToken>()).Returns(
        [
            Track(1, "Ace of Spades", "Motörhead", "No Remorse"),
            Track(2, "Yesterday", "The Beatles", "Help!")
        ]);
        _albums.GetAlbums(Arg.Any<CancellationToken>()).Returns(
        [
            new AlbumOnlyDto { Id = 1, Title = "No Remorse", Artists = [new ArtistOnlyDto { Name = "Motörhead" }] }
        ]);
        _artists.GetArtists(Arg.Any<CancellationToken>()).Returns(
        [
            new ArtistOnlyDto { Id = 1, Name = "Motörhead" }
        ]);

        var result = await CreateSut().Search("motorhead", 50, CancellationToken.None);

        result.Tracks.ShouldHaveSingleItem().Id.ShouldBe(1u);
        result.Albums.ShouldHaveSingleItem().Id.ShouldBe(1u);
        result.Artists.ShouldHaveSingleItem().Id.ShouldBe(1u);
    }

    [Fact]
    public async Task Search_CapsEachSection()
    {
        _tracks.GetAll(Arg.Any<CancellationToken>()).Returns(
            Enumerable.Range(0, 100).Select(i => Track((uint)i, $"Song {i}", "Artist", "Album")).ToArray());
        _albums.GetAlbums(Arg.Any<CancellationToken>()).Returns([]);
        _artists.GetArtists(Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateSut().Search("song", 10, CancellationToken.None);

        result.Tracks.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Search_RanksExactArtistMatch_AboveArtistThatOnlyContainsTheQuery()
    {
        _tracks.GetAll(Arg.Any<CancellationToken>()).Returns([]);
        _albums.GetAlbums(Arg.Any<CancellationToken>()).Returns([]);
        _artists.GetArtists(Arg.Any<CancellationToken>()).Returns(
        [
            new ArtistOnlyDto { Id = 1, Name = "Andrew Rayel" },
            new ArtistOnlyDto { Id = 2, Name = "RAYE" }
        ]);

        var result = await CreateSut().Search("RAYE", 50, CancellationToken.None);

        result.Artists.Select(a => a.Id).ShouldBe([2u, 1u]);
    }

    [Fact]
    public async Task Search_MatchesWordsAcrossTitleAndArtist_RegardlessOfOrder()
    {
        _tracks.GetAll(Arg.Any<CancellationToken>()).Returns(
        [
            Track(1, "Escapism", "RAYE", "Escapism (feat. 070 Shake)")
        ]);
        _albums.GetAlbums(Arg.Any<CancellationToken>()).Returns([]);
        _artists.GetArtists(Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateSut().Search("escapism raye", 50, CancellationToken.None);

        result.Tracks.ShouldHaveSingleItem().Id.ShouldBe(1u);
    }

    [Fact]
    public async Task Search_TrimsTrailingWhitespace()
    {
        _tracks.GetAll(Arg.Any<CancellationToken>()).Returns([Track(1, "Escapism", "RAYE", "Escapism")]);
        _albums.GetAlbums(Arg.Any<CancellationToken>()).Returns([]);
        _artists.GetArtists(Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateSut().Search("raye ", 50, CancellationToken.None);

        result.Tracks.ShouldHaveSingleItem().Id.ShouldBe(1u);
    }

    [Fact]
    public async Task Search_BlankQuery_ReturnsEmpty_WithoutHittingRepositories()
    {
        var result = await CreateSut().Search("   ", 50, CancellationToken.None);

        result.Tracks.ShouldBeEmpty();
        await _tracks.DidNotReceive().GetAll(Arg.Any<CancellationToken>());
    }

    private static TrackDto Track(uint id, string title, string artist, string album)
    {
        return new()
        {
            Id = id,
            Title = title,
            Artists = [new ArtistOnlyDto { Id = id, Name = artist }],
            Album = album,
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
