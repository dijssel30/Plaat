using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plaat.Db.Models;

namespace Plaat.Db.Configurations;

public sealed class TrackRatingConfiguration : IEntityTypeConfiguration<TrackRating>
{
    public void Configure(EntityTypeBuilder<TrackRating> builder)
    {
        builder.HasKey(tr => new { tr.UserId, tr.TrackId });

        builder.HasOne(tr => tr.User)
            .WithMany()
            .HasForeignKey(tr => tr.UserId);

        builder.HasOne(tr => tr.Track)
            .WithMany(t => t.Ratings)
            .HasForeignKey(tr => tr.TrackId);
    }
}
