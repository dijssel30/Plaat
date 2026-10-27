using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        // Stored as ticks rather than the default TEXT conversion: SQLite can't translate
        // ORDER BY over DateTimeOffset columns, but it can over the underlying long.
        builder.Property(t => t.Timestamp)
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero))
            .IsRequired();

        builder.Property(t => t.Level)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.Category)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(t => t.Message)
            .IsRequired();

        builder.HasIndex(t => t.Timestamp);
    }
}
