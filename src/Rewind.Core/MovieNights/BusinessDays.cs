using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;

namespace Rewind.Core.MovieNights;

/// <summary>
/// Opens the movie night at the first member card scanned at Checkout, and closes it at Close Out (Build
/// Plan 3.2), or, when nobody closed it out, at the card that opens the next night (ADR-0014). A return opens
/// none. Nothing else decides which night it is, so a "once a night" rule only has to ask for the open one.
/// </summary>
public sealed partial class BusinessDays(
    IDbContextFactory<RewindDb> dbFactory,
    TimeProvider time,
    ILogger<BusinessDays> logger) : IBusinessDays
{
    // ADR-0014: no movie night lasts this long, even one that runs past midnight, and the next evening's first
    // card is further away than that. So a night open for longer is one nobody closed out.
    private static readonly TimeSpan LongestNight = TimeSpan.FromHours(16);

    /// <inheritdoc />
    public async Task<BusinessDayRow> OpenOrCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await FindOrOpenAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            // DATA-11: when FindOrOpenAsync looked, no night was open, or only one nobody closed out, and
            // another scan opened one before the save. The unique index is the real guard, and the refused
            // save took this scan's close of the old night with it (DATA-7). Anything else it refused would
            // be a bug.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindOpenAsync(db, cancellationToken) is { } open)
            {
                return open;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<BusinessDayRow?> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime; // NET-8, DATA-8
        var open = await FindOpenAsync(db, cancellationToken);
        return open is not null && IsTonights(open, now) ? open : null;
    }

    /// <inheritdoc />
    public async Task<BusinessDayClosing> CloseAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var day = await db.BusinessDays.SingleOrDefaultAsync(d => d.ClosedAt == null, cancellationToken);
        if (day is null)
        {
            return new BusinessDayClosing(BusinessDayClosingStatus.NothingOpen, null);
        }

        day.ClosedAt = time.GetUtcNow().UtcDateTime; // NET-8, DATA-8
        await db.SaveChangesAsync(cancellationToken);
        LogClosed(logger, day.Id);
        return new BusinessDayClosing(BusinessDayClosingStatus.Closed, Row(day));
    }

    // DATA-12: the open night is read untracked, since a scan of it only reads. Only a stale one is loaded
    // tracked, to be closed in this save.
    private async Task<BusinessDayRow> FindOrOpenAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime; // NET-8, DATA-8
        var open = await FindOpenAsync(db, cancellationToken);
        if (open is not null && IsTonights(open, now))
        {
            return open;
        }

        // ADR-0014: a night that's been open that long is one nobody closed out, so this card ends it. Its
        // close and the new night are one save (DATA-7). EF Core sends the update before the insert, whichever
        // it was given first, so the one-open-night index never sees two open nights, as
        // OpenOrCurrentAsync_StaleNight_EndsItWithNoError pins.
        if (open is not null)
        {
            var stale = await db.BusinessDays.SingleAsync(d => d.Id == open.Id, cancellationToken);
            stale.ClosedAt = now;
        }

        var day = new BusinessDay { OpenedAt = now };
        db.BusinessDays.Add(day);
        await db.SaveChangesAsync(cancellationToken);
        if (open is not null)
        {
            LogClosedStale(logger, open.Id, TimeZoneInfo.ConvertTimeFromUtc(open.OpenedAt, time.LocalTimeZone));
        }

        LogOpened(logger, day.Id);
        return Row(day);
    }

    // ADR-0014: the open night is still tonight's for up to LongestNight after it opened, exactly that long included,
    // whichever call asks. The rule trusts the register's clock, so one set back to before the night opened makes the
    // night look newer, and it's still tonight's.
    private static bool IsTonights(BusinessDayRow open, DateTime now) => now - open.OpenedAt <= LongestNight;

    private static Task<BusinessDayRow?> FindOpenAsync(RewindDb db, CancellationToken cancellationToken) =>
        db.BusinessDays
            .AsNoTracking()
            .Where(d => d.ClosedAt == null)
            .Select(d => new BusinessDayRow(d.Id, d.OpenedAt, d.ClosedAt))
            .SingleOrDefaultAsync(cancellationToken);

    private static BusinessDayRow Row(BusinessDay day) => new(day.Id, day.OpenedAt, day.ClosedAt);

    // Information (OBS-3): each night's start and end are what the rest of its log reads against.
    [LoggerMessage(Level = LogLevel.Information, Message = "Opened business day {BusinessDayId}")]
    private static partial void LogOpened(ILogger logger, int businessDayId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed business day {BusinessDayId}")]
    private static partial void LogClosed(ILogger logger, int businessDayId);

    // Warning (OBS-3): a handled problem, in place of the line above for a night Close Out would have closed. It
    // says when the night opened on the register's clock (DATA-8), as the log's own times are, so the Store
    // Manager can tell which movie night went without its Close Out.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Closed business day {BusinessDayId}, which opened {OpenedAt:yyyy-MM-dd HH:mm} and was never closed out; the next Close Out backs it up")]
    private static partial void LogClosedStale(ILogger logger, int businessDayId, DateTime openedAt);
}
