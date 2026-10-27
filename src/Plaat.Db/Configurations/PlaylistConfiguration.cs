using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.ToTable("PlayLists");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId);
    }
}
