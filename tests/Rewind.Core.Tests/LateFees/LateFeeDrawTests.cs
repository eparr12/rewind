using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Tests.LateFees;

public sealed class LateFeeDrawTests
{
    private static readonly DateTime MovieNight = new(2026, 10, 24, 2, 15, 0, DateTimeKind.Utc);

    // DATA-11: a rescan can't fish for a Clear, whatever the service does.
    [Fact]
    public async Task SaveChanges_SecondDrawForOneMemberAndNight_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var (member, night) = await AddMemberAndNightAsync(db, ct);
        db.LateFeeDraws.Add(Draw(member, night, Card.Clear));
        await db.SaveChangesAsync(ct);

        db.LateFeeDraws.Add(Draw(member, night, Card.Clear));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // Stored by name: a number cast to Card would otherwise go in as its digits.
    [Fact]
    public async Task SaveChanges_CardItDoesNotKnow_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var (member, night) = await AddMemberAndNightAsync(db, ct);

        db.LateFeeDraws.Add(Draw(member, night, (Card)7));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // Only an Overdue card has an alert to name a movie in.
    [Fact]
    public async Task SaveChanges_ClearDrawWithAFlavorMovie_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var (member, night) = await AddMemberAndNightAsync(db, ct);
        var movie = await AddMovieAsync(db, ct);

        db.LateFeeDraws.Add(Draw(member, night, Card.Clear, movie));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // The collection can be empty, so an Overdue card doesn't have to name a movie.
    [Fact]
    public async Task SaveChanges_OverdueDrawWithNoFlavorMovie_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var (member, night) = await AddMemberAndNightAsync(db, ct);
        db.LateFeeDraws.Add(Draw(member, night, Card.Overdue));

        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await db.LateFeeDraws.CountAsync(ct));
    }

    // DATA-4: a member's draws are history, so they keep the member, the night and the movie they point at.
    [Fact]
    public async Task SaveChanges_DeletingMemberWithADraw_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddDrawAsync(rewind, ct);

        // A fresh context tracks no draw, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.Members.Remove(await fresh.Members.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DeletingNightWithADraw_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddDrawAsync(rewind, ct);

        await using var fresh = rewind.OpenDb();
        fresh.BusinessDays.Remove(await fresh.BusinessDays.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DeletingMovieThatFlavorsADraw_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddDrawAsync(rewind, ct);

        await using var fresh = rewind.OpenDb();
        fresh.Movies.Remove(await fresh.Movies.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    private static LateFeeDraw Draw(int member, int night, Card card, int? flavorMovie = null) =>
        new() { MemberId = member, BusinessDayId = night, Card = card, FlavorMovieId = flavorMovie, DrawnAt = MovieNight };

    private static async Task<(int Member, int Night)> AddMemberAndNightAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var member = new Member { Name = "Dad", CardBarcode = "MEM-001" };
        var night = new BusinessDay { OpenedAt = MovieNight };
        db.Members.Add(member);
        db.BusinessDays.Add(night);
        await db.SaveChangesAsync(cancellationToken);
        return (member.Id, night.Id);
    }

    private static async Task<int> AddMovieAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var movie = new Movie { Title = "Halloween" };
        db.Movies.Add(movie);
        await db.SaveChangesAsync(cancellationToken);
        return movie.Id;
    }

    // An Overdue draw, with the member, the night and the movie it points at, saved before the test.
    private static async Task AddDrawAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var (member, night) = await AddMemberAndNightAsync(db, cancellationToken);
        var movie = await AddMovieAsync(db, cancellationToken);
        db.LateFeeDraws.Add(Draw(member, night, Card.Overdue, movie));
        await db.SaveChangesAsync(cancellationToken);
    }
}
