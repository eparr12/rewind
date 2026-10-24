using Rewind.Core.Catalog;

namespace Rewind.Core.Tests.Catalog;

public sealed class GenreTests
{
    [Fact]
    public async Task SaveChanges_DuplicateTmdbId_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Genres.Add(new Genre { TmdbId = 27, Name = "Horror" });
        await db.SaveChangesAsync(ct);

        db.Genres.Add(new Genre { TmdbId = 27, Name = "Horror" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveChanges_BlankName_IsRefused(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Genres.Add(new Genre { TmdbId = 27, Name = name });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }
}
