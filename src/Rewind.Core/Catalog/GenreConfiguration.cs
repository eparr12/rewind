using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Catalog;

/// <summary>
/// The database holds the genres' rules it can: one genre per TMDB ID, and a name that isn't
/// blank (DATA-11).
/// </summary>
public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.HasIndex(g => g.TmdbId).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_Genres_Name", "trim(\"Name\") <> ''"));
    }
}
