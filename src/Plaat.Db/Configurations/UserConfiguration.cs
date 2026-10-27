using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.Property(t => t.Username)
            .HasMaxLength(255)
            .UseCollation("NOCASE")
            .IsRequired();

        builder.Property(t => t.PasswordHash)
            .HasMaxLength(512)
            .IsRequired();

        builder.HasMany(t => t.RefreshTokens)
            .WithOne(t => t.User);

        builder.HasIndex(t => t.Username)
            .IsUnique();
    }
}
