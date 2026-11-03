using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;

namespace Rewind.Core.MovieNights;

/// <summary>
/// Opens the movie night at its first scan and closes it at Close Out (Build Plan 3.2). Nothing else
/// decides which night it is, so a "once a night" rule only has to ask for the open one.
/// </summary>
public sealed partial class BusinessDays(
    IDbContextFactory<RewindDb> dbFactory,
    TimeProvider time,
    ILogger<BusinessDays> logger) : IBusinessDays
{
    /// <inheritdoc />
    public async Task<BusinessDayRow> OpenOrCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await FindOrOpenAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            // DATA-11: no night was open when FindOrOpenAsync looked, and another scan opened one before
            // the save. The unique index is the real guard. Anything else it refused would be a bug.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindOpenAsync(db, cancellationToken) is { } open)
            {
                return open;
            }

            throw;
        }
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

    private async Task<BusinessDayRow> FindOrOpenAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await FindOpenAsync(db, cancellationToken) is { } open)
        {
            return open;
        }

        var day = new BusinessDay { OpenedAt = time.GetUtcNow().UtcDateTime }; // NET-8, DATA-8
        db.BusinessDays.Add(day);
        await db.SaveChangesAsync(cancellationToken);
        LogOpened(logger, day.Id);
        return Row(day);
    }

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
}
