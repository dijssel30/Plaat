using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class TrackLocationConfiguration : IEntityTypeConfiguration<TrackLocation>
{
    public void Configure(EntityTypeBuilder<TrackLocation> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.Property(t => t.Path)
            .HasMaxLength(1024)
            .IsRequired();

        builder.HasOne(t => t.Track)
            .WithMany(t => t.TrackLocations)
            .HasForeignKey(t => t.TrackId);

        builder.HasIndex(t => t.Path)
            .IsUnique();
    }
}
