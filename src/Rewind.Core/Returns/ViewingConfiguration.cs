using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Checkout;
using Rewind.Core.Members;

namespace Rewind.Core.Returns;

/// <summary>
/// The database holds the watch log's rules it can (DATA-11): a rental item and a member it knows, each member
/// once per rental item, and stars from 1 to 5 or none.
/// </summary>
public sealed class ViewingConfiguration : IEntityTypeConfiguration<Viewing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Viewing> builder)
    {
        builder.HasOne<RentalItem>().WithMany().HasForeignKey(v => v.RentalItemId);
        builder.HasOne<Member>().WithMany().HasForeignKey(v => v.MemberId);

        // One row per person per rental item. RentalItemId leads, so the index also serves that foreign key.
        builder.HasIndex(v => new { v.RentalItemId, v.MemberId }).IsUnique();

        // A CHECK lets NULL through, so a skipped rating is saved as no stars, never 0 (DATA-3).
        builder.ToTable(t => t.HasCheckConstraint("CK_WatchLog_Stars", $"\"Stars\" BETWEEN {Viewing.MinStars} AND {Viewing.MaxStars}"));
    }
}
