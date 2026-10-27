using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.Property(t => t.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasMany(t => t.Tracks)
            .WithMany(t => t.Genres);

        builder.HasIndex(t => t.Name)
            .IsUnique();
    }
}
