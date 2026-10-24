using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Metadata;

/// <summary>
/// The database holds the lookups' rules it can: one answer per barcode, each barcode in Rewind's
/// form, and no blank title, since not found is a NULL title (DATA-3, DATA-11).
/// </summary>
public sealed class UpcLookupConfiguration : IEntityTypeConfiguration<UpcLookup>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UpcLookup> builder)
    {
        builder.HasIndex(u => u.Upc).IsUnique();
        builder.ToTable(t =>
        {
            // The form Upc.TryNormalize gives a factory barcode: 12 or 13 digits, so never blank.
            // CaseConfiguration holds Cases.FactoryUpc to the same CHECK; change them together.
            t.HasCheckConstraint(
                "CK_UpcLookups_Upc",
                "length(\"Upc\") IN (12, 13) AND \"Upc\" NOT GLOB '*[^0-9]*'");
            t.HasCheckConstraint("CK_UpcLookups_ProductTitle", "trim(\"ProductTitle\") <> ''");
        });
    }
}
