using Microsoft.EntityFrameworkCore;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Tests.MovieNights;

public sealed class BusinessDayTests
{
    private static readonly DateTime MovieNight = new(2026, 10, 24, 2, 15, 0, DateTimeKind.Utc);

    // DATA-11, and why the index isn't on ClosedAt: SQLite treats NULLs as distinct in a unique index, so
    // one there, even filtered to the NULLs, would let this second row through.
    [Fact]
    public async Task SaveChanges_SecondOpenDay_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.BusinessDays.Add(new BusinessDay { OpenedAt = MovieNight });
        await db.SaveChangesAsync(ct);

        db.BusinessDays.Add(new BusinessDay { OpenedAt = MovieNight.AddDays(7) });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // The other way to get a second open night, and the column is generated, so the index hears of it.
    [Fact]
    public async Task SaveChanges_ReopeningADayWhileAnotherIsOpen_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var lastWeek = new BusinessDay { OpenedAt = MovieNight, ClosedAt = MovieNight.AddHours(3) };
        db.BusinessDays.AddRange(lastWeek, new BusinessDay { OpenedAt = MovieNight.AddDays(7) });
        await db.SaveChangesAsync(ct);

        lastWeek.ClosedAt = null;

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // The rule is about open nights: closed ones share the column's other value, and even a closing time,
    // and the index leaves them out.
    [Fact]
    public async Task SaveChanges_ClosedDaysBesideAnOpenOne_AreSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var closedAt = MovieNight.AddHours(3);
        db.BusinessDays.AddRange(
            new BusinessDay { OpenedAt = MovieNight, ClosedAt = closedAt },
            new BusinessDay { OpenedAt = MovieNight, ClosedAt = closedAt },
            new BusinessDay { OpenedAt = MovieNight });

        await db.SaveChangesAsync(ct);

        Assert.Equal(3, await db.BusinessDays.CountAsync(ct));
    }
}
