using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class SmartPlaylistRuleGroupConfiguration : IEntityTypeConfiguration<SmartPlaylistRuleGroup>
{
    public void Configure(EntityTypeBuilder<SmartPlaylistRuleGroup> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id)
            .ValueGeneratedOnAdd();

        builder.ToTable("SmartPlaylistRuleGroup");

        builder.HasOne(g => g.ParentGroup)
            .WithMany(g => g.ChildGroups)
            .HasForeignKey(g => g.ParentGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
