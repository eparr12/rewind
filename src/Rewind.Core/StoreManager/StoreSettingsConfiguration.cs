using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.StoreManager;

/// <summary>
/// Keeps StoreSettings to one row and seeds it with the opening prices from the Overview.
/// </summary>
public sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoreSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.ToTable(t => t.HasCheckConstraint("CK_StoreSettings_SingleRow", $"\"Id\" = {StoreSettings.SingleRowId}"));

        // The migration inserts this row once. After that the prices belong to the Store
        // Manager, so never change them here: the next migration would overwrite the family's prices.
        builder.HasData(new StoreSettings
        {
            Id = StoreSettings.SingleRowId,
            StoreName = "Rewind Video",
            RentalPriceCents = 200,
            LateFeeCents = 100,
        });
    }
}
