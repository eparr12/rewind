using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Catalog;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Checkout;

/// <summary>
/// The database holds the rental items' rules it can (DATA-11): a movie the case really holds, a case once
/// per sale, one open rental per case, and a price in range.
/// </summary>
public sealed class RentalItemConfiguration : IEntityTypeConfiguration<RentalItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RentalItem> builder)
    {
        // The pair points at a row of CaseMovies, so a box-set pick can only be a movie that's in that case.
        // Once a case has been rented, its CaseMovies rows are history too, so a wrong match on a rented case
        // is fixed by retiring the case and shelving a new one (DATA-4).
        builder.HasOne<CaseMovie>().WithMany().HasForeignKey(i => new { i.CaseId, i.MovieId });

        builder.HasIndex(i => new { i.RentalId, i.CaseId }).IsUnique();

        // One open rental per case: a case that's out can't go out again until it's back, whichever movie is
        // picked from it. The index ADR-0009 turned down, on ClosedAt and filtered to the NULLs, couldn't hold
        // its rule, because the column it indexed was the NULL one, and SQLite treats NULLs as distinct in a
        // unique index. This one indexes CaseId, which is never NULL, and the filter only picks the rows, so
        // two items out on one case collide, whether the second is a new item or a return cleared.
        builder.HasIndex(i => i.CaseId).IsUnique().HasFilter("\"ReturnedAt\" IS NULL");

        // The same limit as every other price (StoreSettings.MaxPriceCents).
        builder.ToTable(t => t.HasCheckConstraint("CK_RentalItems_PriceCents", $"\"PriceCents\" BETWEEN 0 AND {StoreSettings.MaxPriceCents}"));
    }
}
