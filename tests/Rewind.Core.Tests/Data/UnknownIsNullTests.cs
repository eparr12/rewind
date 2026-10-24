using Rewind.Core.Catalog;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Data;

public sealed class UnknownIsNullTests
{
    // Unknown is NULL, never empty or blank text (DATA-3), in every column that can be unknown.
    [Theory]
    [InlineData(nameof(Movie.Director), "")]
    [InlineData(nameof(Movie.Director), " ")]
    [InlineData(nameof(Movie.Overview), "")]
    [InlineData(nameof(Movie.Overview), " ")]
    [InlineData(nameof(Movie.PosterFile), "")]
    [InlineData(nameof(Movie.PosterFile), " ")]
    [InlineData(nameof(Case.FactoryUpc), "")]
    [InlineData(nameof(Case.FactoryUpc), " ")]
    [InlineData(nameof(UpcLookup.ProductTitle), "")]
    [InlineData(nameof(UpcLookup.ProductTitle), " ")]
    public async Task SaveChanges_BlankForUnknown_IsRefused(string column, string blank)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        object row = column switch
        {
            nameof(Movie.Director) => new Movie { Title = "Halloween", Director = blank },
            nameof(Movie.Overview) => new Movie { Title = "Halloween", Overview = blank },
            nameof(Movie.PosterFile) => new Movie { Title = "Halloween", PosterFile = blank },
            nameof(Case.FactoryUpc) => new Case { StoreLabel = "RV-00001", FactoryUpc = blank },
            nameof(UpcLookup.ProductTitle) => new UpcLookup
            {
                Upc = "036000291452",
                ProductTitle = blank,
                LookedUpAt = rewind.Time.GetUtcNow().UtcDateTime,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "No row to save for that column."),
        };

        db.Add(row);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }
}
