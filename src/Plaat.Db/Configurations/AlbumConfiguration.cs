using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.Property(r => r.Title)
            .HasMaxLength(255);

        builder.HasOne(a => a.CoverArt)
            .WithMany(c => c.Albums)
            .HasForeignKey(a => a.CoverArtId);

        builder.HasMany(t => t.AlbumArtists)
            .WithMany(a => a.Albums);

        builder.HasIndex(t => t.Title);
    }
}
