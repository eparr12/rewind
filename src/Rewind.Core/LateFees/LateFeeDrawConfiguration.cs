using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Catalog;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;

namespace Rewind.Core.LateFees;

/// <summary>
/// The database holds the draws' rules it can (DATA-11): one draw per member per night, a card it knows, and
/// a flavor movie only on an Overdue card.
/// </summary>
public sealed class LateFeeDrawConfiguration : IEntityTypeConfiguration<LateFeeDraw>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LateFeeDraw> builder)
    {
        builder.HasOne<Member>().WithMany().HasForeignKey(d => d.MemberId);
        builder.HasOne<BusinessDay>().WithMany().HasForeignKey(d => d.BusinessDayId);
        builder.HasOne<Movie>().WithMany().HasForeignKey(d => d.FlavorMovieId);

        // What keeps a rescan from fishing for a Clear, however the service is called.
        builder.HasIndex(d => new { d.MemberId, d.BusinessDayId }).IsUnique();

        // Stored by name, with a CHECK built from the enum's names, as CaseConfiguration does for a case's
        // status, and for the same reasons. A number cast to Card would otherwise be stored as its digits.
        var cards = string.Join(", ", Enum.GetNames<Card>().Select(card => $"'{card}'"));
        builder.Property(d => d.Card).HasConversion<string>();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_LateFeeDraws_Card", $"\"Card\" IN ({cards})");

            // Only an Overdue card has an alert to name a movie in.
            t.HasCheckConstraint("CK_LateFeeDraws_FlavorMovie", $"\"Card\" = '{nameof(Card.Overdue)}' OR \"FlavorMovieId\" IS NULL");
        });
    }
}
