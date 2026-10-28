using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Members;

/// <summary>
/// The database holds the members' rules it can (DATA-11): one member per card, a card in the form
/// Barcodes reads as a member card, and a name that isn't blank.
/// </summary>
public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.HasIndex(m => m.CardBarcode).IsUnique();
        builder.ToTable(t =>
        {
            // The format Barcodes.Classify reads as a member card: MEM- and three digits.
            t.HasCheckConstraint("CK_Members_CardBarcode", "\"CardBarcode\" GLOB 'MEM-[0-9][0-9][0-9]'");
            t.HasCheckConstraint("CK_Members_Name", "trim(\"Name\") <> ''");
        });
    }
}
