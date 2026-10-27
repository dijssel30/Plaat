using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class TrackDtoTests
{
    [Fact]
    public void AllArtists_WithSingleArtist_ReturnsSingleName()
    {
        var dto = MakeTrack([new ArtistOnlyDto { Id = 1, Name = "Artist One" }]);

        dto.AllArtists.ShouldBe("Artist One");
    }

    [Fact]
    public void AllArtists_WithMultipleArtists_ReturnsCommaSeparated()
    {
        var dto = MakeTrack([
            new ArtistOnlyDto { Id = 1, Name = "Artist One" },
            new ArtistOnlyDto { Id = 2, Name = "Artist Two" }
        ]);

        dto.AllArtists.ShouldBe("Artist One, Artist Two");
    }

    [Fact]
    public void AllArtists_WithNoArtists_ReturnsEmptyString()
    {
        var dto = MakeTrack([]);

        dto.AllArtists.ShouldBeEmpty();
    }

    private static TrackDto MakeTrack(ICollection<ArtistOnlyDto> artists)
    {
        return new()
        {
            Id = 1,
            Title = "Test Track",
            Artists = artists,
            Album = "Test Album",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2024,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
