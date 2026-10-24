using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Catalog;

/// <summary>
/// The database holds the cases' rules it can: one case per store label, in the label's format, a
/// factory UPC in Rewind's form, a status it knows, and which movies each case holds (DATA-11).
/// </summary>
public sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.HasIndex(c => c.StoreLabel).IsUnique();

        // Indexed but not unique: a UPC names an edition, and two copies of one edition share it.
        builder.HasIndex(c => c.FactoryUpc);

        // Stored by name, and the CHECK refuses anything else: a status another tool wrote, or a
        // number cast to CaseStatus, which EF Core stores as its digits.
        //
        // The CHECK is built from the enum's names, so it's part of the model. Adding, renaming or
        // reordering a status needs a migration, which rebuilds Cases. Until there is one, the model
        // test in RewindDbTests fails, and a start with a migration to apply fails, because EF Core
        // won't migrate a model that has changed since its last migration. A database that's up to
        // date still starts, since DatabaseStartup only migrates when a migration is pending, and the
        // old CHECK then refuses the new status. DATA-13 doesn't apply: this is DDL from names the
        // compiler fixes, not a query built from input.
        var statuses = string.Join(", ", Enum.GetNames<CaseStatus>().Select(status => $"'{status}'"));
        builder.Property(c => c.Status).HasConversion<string>();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Cases_Status", $"\"Status\" IN ({statuses})");

            // The format Barcodes.Classify reads as a store label: RV- and five digits.
            t.HasCheckConstraint("CK_Cases_StoreLabel", "\"StoreLabel\" GLOB 'RV-[0-9][0-9][0-9][0-9][0-9]'");

            // The form Upc.TryNormalize gives a factory barcode: 12 or 13 digits, so never blank.
            // UpcLookupConfiguration holds UpcLookups.Upc to the same CHECK; change them together.
            t.HasCheckConstraint(
                "CK_Cases_FactoryUpc",
                "length(\"FactoryUpc\") IN (12, 13) AND \"FactoryUpc\" NOT GLOB '*[^0-9]*'");
        });

        builder.HasMany(c => c.Movies)
            .WithMany(m => m.Cases)
            .UsingEntity<CaseMovie>(j => j.HasKey(cm => new { cm.CaseId, cm.MovieId }));
    }
}
