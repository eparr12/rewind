using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Concessions;
using Rewind.Core.LateFees;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Checkout;

/// <summary>
/// The database holds the sale lines' rules it can (DATA-11): a line is a snack or a late fee and never both,
/// a draw's fee is paid once (ADR-0010), a name that isn't blank or too long, and a price in range.
/// </summary>
public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.HasOne<ConcessionItem>().WithMany().HasForeignKey(l => l.ConcessionItemId);
        builder.HasOne<LateFeeDraw>().WithMany().HasForeignKey(l => l.LateFeeDrawId);

        // SQLite treats NULLs as distinct in a unique index, so any number of snack lines can have no draw,
        // and only fee lines can collide.
        builder.HasIndex(l => l.LateFeeDrawId).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SaleLines_Name", $"trim(\"Name\") <> '' AND length(\"Name\") <= {SaleLine.MaxNameLength}");

            // The same limit as every other price (StoreSettings.MaxPriceCents).
            t.HasCheckConstraint("CK_SaleLines_PriceCents", $"\"PriceCents\" BETWEEN 0 AND {StoreSettings.MaxPriceCents}");

            // Exactly one of the two, so every line can say what it sold.
            t.HasCheckConstraint("CK_SaleLines_Source", "(\"ConcessionItemId\" IS NULL) <> (\"LateFeeDrawId\" IS NULL)");
        });
    }
}
