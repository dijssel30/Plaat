using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.Property(t => t.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.Property(t => t.ReplacedByTokenHash)
            .HasMaxLength(64);

        builder.Property(t => t.FamilyId)
            .HasMaxLength(32)
            .IsRequired();

        // Reuse detection revokes by family, so that lookup has to be cheap.
        builder.HasIndex(t => t.FamilyId);

        builder.HasOne(t => t.User)
            .WithMany(t => t.RefreshTokens);
    }
}
