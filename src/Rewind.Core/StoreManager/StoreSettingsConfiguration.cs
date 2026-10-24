using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.StoreManager;

/// <summary>
/// Keeps StoreSettings to one row. The Initial migration inserts it with the opening prices
/// from the Overview, and from then on the Store Manager edits it.
/// </summary>
public sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoreSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.ToTable(t => t.HasCheckConstraint("CK_StoreSettings_SingleRow", $"\"Id\" = {StoreSettings.SingleRowId}"));

        // No HasData for the row: seed data is part of the model, so a later migration could
        // update or delete it and overwrite the family's prices (DATA-5).
    }
}
