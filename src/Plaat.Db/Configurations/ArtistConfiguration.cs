using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.Property(r => r.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.NormalizedName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.DiscogsImagePath)
            .HasMaxLength(255);

        builder.Property(r => r.DiscogsLookupStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasMany(t => t.Tracks)
            .WithMany(t => t.Artists);

        builder.HasMany(t => t.Albums)
            .WithMany(t => t.AlbumArtists);

        builder.HasIndex(t => t.Name)
            .IsUnique();

        builder.HasIndex(t => t.NormalizedName)
            .IsUnique();
    }
}
