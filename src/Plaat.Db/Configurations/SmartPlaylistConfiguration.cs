using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class SmartPlaylistConfiguration : IEntityTypeConfiguration<SmartPlaylist>
{
    public void Configure(EntityTypeBuilder<SmartPlaylist> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.ToTable("SmartPlayLists");

        builder.HasOne(t => t.RootGroup)
            .WithMany()
            .HasForeignKey(t => t.RootGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId);
    }
}
