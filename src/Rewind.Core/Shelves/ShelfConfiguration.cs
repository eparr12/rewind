using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Shelves;

/// <summary>
/// The database holds the shelves' rules it can (DATA-11): each kind of shelf to its own columns, one Staff Picks
/// shelf for each member, a seasonal shelf's name not blank and no other's whatever its letter case, and each end of
/// a season a real day (ADR-0015).
/// </summary>
public sealed class ShelfConfiguration : IEntityTypeConfiguration<Shelf>
{
    // A member's shelf is named for its member and has no season. It's never deactivated by itself, since it's hidden
    // while its member is deactivated.
    private const string StaffPicks =
        "\"MemberId\" IS NOT NULL AND \"Name\" IS NULL AND \"FromMonth\" IS NULL AND \"FromDay\" IS NULL" +
        " AND \"ToMonth\" IS NULL AND \"ToDay\" IS NULL AND \"IsActive\"";

    // A seasonal shelf has a name and all four numbers of its season, active or not.
    private const string Seasonal =
        "\"MemberId\" IS NULL AND \"Name\" IS NOT NULL AND \"FromMonth\" IS NOT NULL AND \"FromDay\" IS NOT NULL" +
        " AND \"ToMonth\" IS NOT NULL AND \"ToDay\" IS NOT NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Shelf> builder)
    {
        // One Staff Picks shelf for each member. A seasonal shelf has no member, and SQLite's unique index lets NULLs
        // repeat, so there can be any number of those. The index also serves the foreign key.
        builder.HasOne(s => s.Member).WithMany().HasForeignKey(s => s.MemberId);
        builder.HasIndex(s => s.MemberId).IsUnique();

        // No two seasonal shelves share a name, whatever its letter case. NOCASE is the column's own collation, so the
        // index, and any comparison or sort of a name, fold letter case alike. It folds A to Z only, so Día de Muertos
        // and DÍA DE MUERTOS are two names (#100). A member's shelf has no name, and NULLs repeat. A deactivated shelf
        // keeps its name, so it's reactivated rather than added again, as a deactivated member keeps their card.
        builder.Property(s => s.Name).UseCollation("NOCASE");
        builder.HasIndex(s => s.Name).IsUnique();

        builder.ToTable(t =>
        {
            // One CHECK for both kinds, since no column says which kind a shelf is: its member does.
            t.HasCheckConstraint("CK_Shelves_Kind", $"({StaffPicks}) OR ({Seasonal})");

            // trim() also refuses a name that's only spaces. NULL passes, for a member's shelf.
            t.HasCheckConstraint("CK_Shelves_Name", "trim(\"Name\") <> ''");

            t.HasCheckConstraint("CK_Shelves_From", RealDay("FromMonth", "FromDay"));
            t.HasCheckConstraint("CK_Shelves_To", RealDay("ToMonth", "ToDay"));
        });
    }

    // Each end of a season is a real day of the year: a month from 1 to 12, and a day from 1 to that month's last,
    // with 29 for February. It's built from Shelf's constants, as CaseConfiguration builds its CHECK from the enum's
    // names, so the two can't drift: a change to them is a model change, which fails the model test until a migration
    // rebuilds Shelves. A month outside 1 to 12 has no arm in the CASE, which then gives NULL, but the month's own
    // BETWEEN has refused it already. Both NULL, for a member's shelf, passes. DATA-13 doesn't apply: this is DDL from
    // constants, not a query built from input.
    private static string RealDay(string month, string day)
    {
        var lastDays = string.Join(
            " ",
            Enumerable.Range(Shelf.FirstMonth, Shelf.LastMonth - Shelf.FirstMonth + 1)
                .Select(m => $"WHEN {m} THEN {Shelf.LastDayOf(m)}"));
        return $"\"{month}\" BETWEEN {Shelf.FirstMonth} AND {Shelf.LastMonth}" +
            $" AND \"{day}\" BETWEEN {Shelf.FirstDay} AND CASE \"{month}\" {lastDays} END";
    }
}
