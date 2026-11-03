using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.MovieNights;

namespace Rewind.Core.LateFees;

/// <summary>
/// Saves each member's one draw a night from their late-fee bag (Build Plan 3.2). <see cref="ShuffleBag"/> holds
/// the rule, and this holds what the rule needs between draws: the bag, and the member's last card.
/// </summary>
public sealed partial class LateFeeDraws(
    IDbContextFactory<RewindDb> dbFactory,
    IBusinessDays days,
    TimeProvider time,
    Random random,
    ILogger<LateFeeDraws> logger) : ILateFeeDraws
{
    /// <inheritdoc />
    public async Task<LateFeeDrawRow> DrawAsync(int memberId, CancellationToken cancellationToken)
    {
        var night = await days.OpenOrCurrentAsync(cancellationToken);
        try
        {
            return await FindOrDrawAsync(memberId, night.Id, cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            // DATA-11: this member had no draw when FindOrDrawAsync looked, and another scan drew first. The
            // unique index is the real guard, and the refused save took its new bag with it (DATA-7).
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindAsync(db, memberId, night.Id, cancellationToken) is { } drawn)
            {
                return drawn;
            }

            throw;
        }
    }

    private async Task<LateFeeDrawRow> FindOrDrawAsync(int memberId, int businessDayId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await FindAsync(db, memberId, businessDayId, cancellationToken) is { } drawn)
        {
            return drawn;
        }

        // Members are never deleted (DATA-4), so an Id from a scanned card is always there.
        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new InvalidOperationException($"There's no member {memberId}, though members are never deleted.");
        var last = await LastCardAsync(db, memberId, cancellationToken);
        var (card, left) = ShuffleBag.Draw(member.Bag, last, random);

        var draw = new LateFeeDraw
        {
            MemberId = memberId,
            BusinessDayId = businessDayId,
            Card = card,
            FlavorMovieId = card == Card.Overdue ? await PickFlavorMovieAsync(db, cancellationToken) : null,
            DrawnAt = time.GetUtcNow().UtcDateTime, // NET-8, DATA-8
        };
        db.LateFeeDraws.Add(draw);
        member.Bag = left;
        await db.SaveChangesAsync(cancellationToken); // DATA-7: the draw and the bag it left, together
        LogDrew(logger, member.CardBarcode, member.Name, card, businessDayId);
        return new LateFeeDrawRow(draw.Id, memberId, businessDayId, card, draw.FlavorMovieId, draw.DrawnAt);
    }

    private static Task<LateFeeDrawRow?> FindAsync(RewindDb db, int memberId, int businessDayId, CancellationToken cancellationToken) =>
        db.LateFeeDraws
            .AsNoTracking()
            .Where(d => d.MemberId == memberId && d.BusinessDayId == businessDayId)
            .Select(d => new LateFeeDrawRow(d.Id, d.MemberId, d.BusinessDayId, d.Card, d.FlavorMovieId, d.DrawnAt))
            .SingleOrDefaultAsync(cancellationToken);

    // By Id, the order the draws happened in, since a clock set back could put DrawnAt out of order.
    private static Task<Card?> LastCardAsync(RewindDb db, int memberId, CancellationToken cancellationToken) =>
        db.LateFeeDraws
            .AsNoTracking()
            .Where(d => d.MemberId == memberId)
            .OrderByDescending(d => d.Id)
            .Select(d => (Card?)d.Card)
            .FirstOrDefaultAsync(cancellationToken);

    // Ordered, so a seeded Random picks the same movie every time (TEST-4).
    private async Task<int?> PickFlavorMovieAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var movies = await db.Movies
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        return movies.Count == 0 ? null : movies[random.Next(movies.Count)];
    }

    // Information (OBS-3): the card a member drew is what they'll ask about, and a late fee on a bill follows it.
    [LoggerMessage(Level = LogLevel.Information, Message = "Member {CardBarcode}, {Name}, drew {Card} on business day {BusinessDayId}")]
    private static partial void LogDrew(ILogger logger, string cardBarcode, string name, Card card, int businessDayId);
}
