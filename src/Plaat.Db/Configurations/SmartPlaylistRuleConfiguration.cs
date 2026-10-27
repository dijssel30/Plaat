using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class SmartPlaylistRuleConfiguration : IEntityTypeConfiguration<SmartPlaylistRule>
{
    public void Configure(EntityTypeBuilder<SmartPlaylistRule> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedOnAdd();

        builder.ToTable("SmartPlayListRule");

        builder.HasDiscriminator<string>("Type")
            .HasValue<PlayCountPlaylistRule>("PlayCountRule")
            .HasValue<TitlePlaylistRule>("TitleRule")
            .HasValue<ArtistNamePlaylistRule>("ArtistNameRule")
            .HasValue<AlbumNamePlaylistRule>("AlbumNameRule")
            .HasValue<GenrePlaylistRule>("GenreRule")
            .HasValue<AddedDateRule>("AddedDateRule")
            .HasValue<AddedWithinDaysPlaylistRule>("AddedWithinDaysRule")
            .HasValue<NotPlayedInDaysPlaylistRule>("NotPlayedInDaysRule")
            .HasValue<DurationRangePlaylistRule>("DurationRangeRule")
            .HasValue<YearPlaylistRule>("YearRule")
            .HasValue<BitratePlaylistRule>("BitrateRule")
            .HasValue<StarsPlaylistRule>("StarsRule");

        builder.HasOne(t => t.Group)
            .WithMany(t => t.Rules)
            .HasForeignKey(t => t.GroupId);
    }
}
