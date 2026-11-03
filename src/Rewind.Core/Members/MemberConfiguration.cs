using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.LateFees;

namespace Rewind.Core.Members;

/// <summary>
/// The database holds the members' rules it can (DATA-11): one member per card, a card in the form
/// Barcodes reads as a member card, a name that isn't blank, and a late-fee bag ShuffleBag can draw from.
/// </summary>
public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.HasIndex(m => m.CardBarcode).IsUnique();

        // At most a full bag of ShuffleBag's letters, with at most one Overdue. It's built from ShuffleBag's
        // constants, as CaseConfiguration builds its CHECK from the enum's names, so the two can't drift: a
        // changed letter or size is a model change, which fails the model test until a migration rebuilds
        // Members. DATA-13 doesn't apply: this is DDL from constants, not a query built from input.
        const char Clear = ShuffleBag.ClearLetter;
        const char Overdue = ShuffleBag.OverdueLetter;
        var bag = $"length(\"Bag\") <= {ShuffleBag.CardsInABag} AND \"Bag\" NOT GLOB '*[^{Clear}{Overdue}]*' AND \"Bag\" NOT GLOB '*{Overdue}*{Overdue}*'";

        builder.ToTable(t =>
        {
            // The format Barcodes.Classify reads as a member card: MEM- and three digits.
            t.HasCheckConstraint("CK_Members_CardBarcode", "\"CardBarcode\" GLOB 'MEM-[0-9][0-9][0-9]'");
            t.HasCheckConstraint("CK_Members_Name", "trim(\"Name\") <> ''");
            t.HasCheckConstraint("CK_Members_Bag", bag);
        });
    }
}
