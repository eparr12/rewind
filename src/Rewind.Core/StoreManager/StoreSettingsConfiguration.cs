using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.StoreManager;

/// <summary>
/// Keeps StoreSettings to one row. The Initial migration inserts it with the opening prices
/// from the Overview, and from then on the Store Manager edits it. Its label and card numbers
/// start at 1.
/// </summary>
public sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoreSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.ToTable(t => t.HasCheckConstraint("CK_StoreSettings_SingleRow", $"\"Id\" = {StoreSettings.SingleRowId}"));

        // The row is older than these columns, so it takes the default the migration adds them with.
        // Left to EF Core, that would be 0, and the first sheet would start at RV-00000 and MEM-000.
        builder.Property(s => s.NextStoreLabelNumber).HasDefaultValue(StoreSettings.FirstNumber);
        builder.Property(s => s.NextMemberCardNumber).HasDefaultValue(StoreSettings.FirstNumber);

        // No HasData for the row: seed data is part of the model, so a later migration could
        // update or delete it and overwrite the family's prices (DATA-5).
    }
}
