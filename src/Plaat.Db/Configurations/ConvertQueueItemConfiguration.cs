using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class ConvertQueueItemConfiguration : IEntityTypeConfiguration<ConvertQueueItem>
{
    public void Configure(EntityTypeBuilder<ConvertQueueItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.HasOne(t => t.TrackLocation)
            .WithMany()
            .HasForeignKey(t => t.TrackLocationId);
    }
}
