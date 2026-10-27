using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.Property(t => t.Title)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(t => t.MimeType)
            .HasMaxLength(64);

        builder.Property(t => t.FileHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(t => t.OriginalContentHash)
            .HasMaxLength(128);

        builder.HasOne(t => t.Album)
            .WithMany(a => a.Tracks)
            .HasForeignKey(t => t.AlbumId);

        builder.HasMany(t => t.Artists)
            .WithMany(a => a.Tracks);

        builder.HasMany(t => t.Genres)
            .WithMany(g => g.Tracks);

        builder.HasMany(t => t.TrackLocations)
            .WithOne(t => t.Track)
            .HasForeignKey(t => t.TrackId);

        builder.HasIndex(t => t.Title);

        builder.HasIndex(t => t.FileHash)
            .IsUnique();

        builder.HasMany(t => t.Plays)
            .WithOne(p => p.Track);
    }
}
