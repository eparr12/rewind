using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.MovieNights;

/// <summary>The database holds the rule it can (DATA-11): only one business day is open at a time.</summary>
public sealed class BusinessDayConfiguration : IEntityTypeConfiguration<BusinessDay>
{
    private const string IsOpen = "IsOpen";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BusinessDay> builder)
    {
        // SQLite treats NULLs as distinct in a unique index, so an index on ClosedAt, even one filtered to
        // the NULLs, lets any number of days stay open. The open days need a value they all share, which
        // this generated column gives them, and the filter leaves the closed days out.
        //
        // Both are in the model, not raw SQL in a migration, because a table rebuild recreates only the
        // indexes the model knows, so it would drop a raw one without a word (ADR-0009). Nothing reads the
        // column, so BusinessDay doesn't have it.
        builder.Property<bool>(IsOpen).HasComputedColumnSql("\"ClosedAt\" IS NULL", stored: false);
        builder.HasIndex(IsOpen).IsUnique().HasFilter($"\"{IsOpen}\"");
    }
}
