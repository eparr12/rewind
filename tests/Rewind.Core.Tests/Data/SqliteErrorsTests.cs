using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;
using Rewind.Core.Data;

namespace Rewind.Core.Tests.Data;

public sealed class SqliteErrorsTests
{
    [Fact]
    public async Task IsUniqueIndexRefusal_SaveAUniqueIndexRefused_IsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Genres.Add(new Genre { TmdbId = 27, Name = "Horror" });
        await db.SaveChangesAsync(ct);
        db.Genres.Add(new Genre { TmdbId = 27, Name = "Horror" });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        Assert.True(SqliteErrors.IsUniqueIndexRefusal(refused));
    }

    // A CHECK is a rule the database holds too, but a service that took it for a unique refusal would
    // answer that the thing was taken, when it was a bug.
    [Fact]
    public async Task IsUniqueIndexRefusal_SaveACheckRefused_IsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Genres.Add(new Genre { TmdbId = 27, Name = " " });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        Assert.False(SqliteErrors.IsUniqueIndexRefusal(refused));
    }

    // Such as a save EF Core gave up on before SQLite answered.
    [Fact]
    public void IsUniqueIndexRefusal_NoSqliteError_IsFalse()
    {
        var refused = new DbUpdateException("The save failed.", new InvalidOperationException("Not SQLite."));

        Assert.False(SqliteErrors.IsUniqueIndexRefusal(refused));
    }

    [Fact]
    public void IsUniqueIndexRefusal_Null_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what IsUniqueIndexRefusal does with one.
        Func<object?> check = () => SqliteErrors.IsUniqueIndexRefusal(null!);

        var refusal = Assert.Throws<ArgumentNullException>(check);
        Assert.Equal("exception", refusal.ParamName);
    }
}
