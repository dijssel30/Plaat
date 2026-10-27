using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class CoverArtConfiguration : IEntityTypeConfiguration<CoverArt>
{
    public void Configure(EntityTypeBuilder<CoverArt> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();
    }
}
