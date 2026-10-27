using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class PlaylistTrackConfiguration : IEntityTypeConfiguration<PlaylistTrack>
{
    public void Configure(EntityTypeBuilder<PlaylistTrack> builder)
    {
        builder.ToTable("PlayListTrack");

        builder.HasKey(pt => new { pt.PlaylistId, pt.TrackId });

        builder.HasOne(pt => pt.Playlist)
            .WithMany(p => p.PlaylistTracks)
            .HasForeignKey(pt => pt.PlaylistId);

        builder.HasOne(pt => pt.Track)
            .WithMany(t => t.PlaylistTracks)
            .HasForeignKey(pt => pt.TrackId);

        builder.HasIndex(pt => new { pt.PlaylistId, pt.Position });
    }
}
