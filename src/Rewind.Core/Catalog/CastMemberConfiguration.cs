using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Catalog;

/// <summary>
/// The database holds the cast's rules it can: one actor per place in a movie's billing, and a
/// name that isn't blank (DATA-11).
/// </summary>
public sealed class CastMemberConfiguration : IEntityTypeConfiguration<CastMember>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CastMember> builder)
    {
        // A refresh replaces a movie's whole cast, and this keeps a bug in it from billing two
        // actors in one place. MovieId leads, so the index also serves the foreign key.
        builder.HasIndex(c => new { c.MovieId, c.BillingOrder }).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_MovieCast_Name", "trim(\"Name\") <> ''"));
    }
}
