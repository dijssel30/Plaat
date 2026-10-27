using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class FavoriteAlbumConfiguration : IEntityTypeConfiguration<FavoriteAlbum>
{
    public void Configure(EntityTypeBuilder<FavoriteAlbum> builder)
    {
        builder.HasKey(fa => new { fa.UserId, fa.AlbumId });

        builder.HasOne(fa => fa.User)
            .WithMany()
            .HasForeignKey(fa => fa.UserId);

        builder.HasOne(fa => fa.Album)
            .WithMany()
            .HasForeignKey(fa => fa.AlbumId);
    }
}
