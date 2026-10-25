using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rewind.Core.Catalog;

/// <summary>
/// The database holds the movies' rules it can: one movie per TMDB ID, a title that isn't blank,
/// a rating that's one of <see cref="UsRatings.All"/>, and NULL for anything unknown, never a
/// stand-in such as 0 or blank text (DATA-3, DATA-11).
/// </summary>
public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        // A new edition joins the movie we already have. Movies entered by hand have no TMDB ID,
        // and SQLite's unique index lets NULLs repeat.
        builder.HasIndex(m => m.TmdbId).IsUnique();

        // One of UsRatings.All, written as it is there, and the CHECK refuses anything else: TMDB's
        // "" and NR, a TV rating such as TV-14, or a rating in another form, such as pg. It's
        // exact because readers such as the spinner's rating cap compare against UsRatings.All as
        // written, so a pg let in would be a rating they can't place. NULL passes an IN check, so
        // an unknown rating stays NULL (DATA-3).
        //
        // The CHECK is built from UsRatings.All, so the list is part of the model. Changing it
        // needs a migration, which rebuilds Movies. Until there is one, the model test in
        // RewindDbTests fails, and so does a start with a migration to apply, because EF Core won't
        // migrate a model that has changed since its last migration. A database that's up to date
        // still starts, and its old CHECK then refuses the new rating. DATA-13 doesn't apply: this
        // is DDL from a list the compiler fixes, not a query built from input.
        var ratings = string.Join(", ", UsRatings.All.Select(rating => $"'{rating}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Movies_Rating", $"\"Rating\" IN ({ratings})");

            // Unknown is NULL (DATA-3). TMDB sends an unknown runtime as 0 and no overview as "",
            // and stored like that they'd slip past filters that skip unknowns, such as the
            // spinner's. trim() also refuses text that's only spaces.
            t.HasCheckConstraint("CK_Movies_Title", "trim(\"Title\") <> ''");
            t.HasCheckConstraint("CK_Movies_Year", "\"Year\" > 0");
            t.HasCheckConstraint("CK_Movies_RuntimeMinutes", "\"RuntimeMinutes\" > 0");
            t.HasCheckConstraint("CK_Movies_Director", "trim(\"Director\") <> ''");
            t.HasCheckConstraint("CK_Movies_Overview", "trim(\"Overview\") <> ''");
            t.HasCheckConstraint("CK_Movies_PosterFile", "trim(\"PosterFile\") <> ''");
        });

        builder.HasMany(m => m.Genres)
            .WithMany()
            .UsingEntity<MovieGenre>(j => j.HasKey(mg => new { mg.MovieId, mg.GenreId }));
    }
}
