using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Catalog;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Checkout;

/// <summary>
/// The database holds the rental items' rules it can (DATA-11): a movie the case really holds, a case once
/// per sale, and a price in range.
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

        // The same limit as every other price (StoreSettings.MaxPriceCents).
        builder.ToTable(t => t.HasCheckConstraint("CK_RentalItems_PriceCents", $"\"PriceCents\" BETWEEN 0 AND {StoreSettings.MaxPriceCents}"));
    }
}
