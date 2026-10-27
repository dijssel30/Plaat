using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class MonitoredFolderConfiguration : IEntityTypeConfiguration<MonitoredFolder>
{
    public void Configure(EntityTypeBuilder<MonitoredFolder> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.Property(t => t.Path)
            .HasMaxLength(1024)
            .IsRequired();

        builder.HasIndex(t => t.Path)
            .IsUnique();
    }
}
