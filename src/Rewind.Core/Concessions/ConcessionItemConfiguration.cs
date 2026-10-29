using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Concessions;

/// <summary>
/// The database holds the concession items' rules it can (DATA-11): one item per code, a code in the form
/// Barcodes reads as a concession, a name that isn't blank, and a price the Store Manager can set.
/// </summary>
public sealed class ConcessionItemConfiguration : IEntityTypeConfiguration<ConcessionItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConcessionItem> builder)
    {
        builder.HasIndex(i => i.Code).IsUnique();
        builder.ToTable(t =>
        {
            // The form Barcodes.Classify reads as a concession: CON- and 1 to 20 capital letters. GLOB is
            // case-sensitive, so a lower-case letter is refused too.
            t.HasCheckConstraint(
                "CK_ConcessionItems_Code",
                $"\"Code\" GLOB 'CON-?*' AND \"Code\" NOT GLOB 'CON-*[^A-Z]*' AND length(\"Code\") <= {ConcessionCodes.MaxLength}");
            t.HasCheckConstraint("CK_ConcessionItems_Name", "trim(\"Name\") <> ''");

            // The same limit as the rental price and the late fee, so one constant bounds every price.
            t.HasCheckConstraint("CK_ConcessionItems_PriceCents", $"\"PriceCents\" BETWEEN 0 AND {StoreSettings.MaxPriceCents}");
        });
    }
}
