using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rewind.Core.Catalog;

namespace Rewind.Core.Shelves;

/// <summary>
/// The database holds the rules it can for what's on the shelves (DATA-11): a shelf and a movie it knows, and each
/// movie once on a shelf.
/// </summary>
public sealed class ShelfMovieConfiguration : IEntityTypeConfiguration<ShelfMovie>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShelfMovie> builder)
    {
        // A movie sits on a shelf once. ShelfId leads, so the key also serves that foreign key.
        builder.HasKey(sm => new { sm.ShelfId, sm.MovieId });
        builder.HasOne<Shelf>().WithMany().HasForeignKey(sm => sm.ShelfId);
        builder.HasOne<Movie>().WithMany().HasForeignKey(sm => sm.MovieId);
    }
}
