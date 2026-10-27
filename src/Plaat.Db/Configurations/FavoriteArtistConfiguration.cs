using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class FavoriteArtistConfiguration : IEntityTypeConfiguration<FavoriteArtist>
{
    public void Configure(EntityTypeBuilder<FavoriteArtist> builder)
    {
        builder.HasKey(fa => new { fa.UserId, fa.ArtistId });

        builder.HasOne(fa => fa.User)
            .WithMany()
            .HasForeignKey(fa => fa.UserId);

        builder.HasOne(fa => fa.Artist)
            .WithMany()
            .HasForeignKey(fa => fa.ArtistId);
    }
}
