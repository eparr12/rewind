using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Catalog;

/// <summary>
/// The database holds the movies' rules it can: one movie per TMDB ID, a title that isn't blank,
/// and NULL for anything unknown, never a stand-in such as 0, "NR" or blank text (DATA-3, DATA-11).
/// </summary>
public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        // A new edition joins the movie we already have. Movies entered by hand have no TMDB ID,
        // and SQLite's unique index lets NULLs repeat.
        builder.HasIndex(m => m.TmdbId).IsUnique();

        // Unknown is NULL (DATA-3). TMDB sends an unknown runtime as 0, no rating as "" or "NR" and
        // no overview as "", and stored like that they'd slip past filters that skip unknowns, such
        // as the spinner's. trim() also refuses text that's only spaces.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Movies_Title", "trim(\"Title\") <> ''");
            t.HasCheckConstraint("CK_Movies_Year", "\"Year\" > 0");
            t.HasCheckConstraint("CK_Movies_RuntimeMinutes", "\"RuntimeMinutes\" > 0");
            t.HasCheckConstraint("CK_Movies_Rating", "trim(\"Rating\") NOT IN ('', 'NR')");
            t.HasCheckConstraint("CK_Movies_Director", "trim(\"Director\") <> ''");
            t.HasCheckConstraint("CK_Movies_Overview", "trim(\"Overview\") <> ''");
            t.HasCheckConstraint("CK_Movies_PosterFile", "trim(\"PosterFile\") <> ''");
        });

        builder.HasMany(m => m.Genres)
            .WithMany()
            .UsingEntity<MovieGenre>(j => j.HasKey(mg => new { mg.MovieId, mg.GenreId }));
    }
}
