using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class PlayConfiguration : IEntityTypeConfiguration<Play>
{
    public void Configure(EntityTypeBuilder<Play> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.HasOne(t => t.Track)
            .WithMany(t => t.Plays);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .IsRequired(false);
    }
}
