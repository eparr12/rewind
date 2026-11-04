using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.Money;
using Rewind.Core.Scans;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Checkout;

/// <summary>
/// Rings up a sale, from the member's card to the change due (Build Plan 3.3). The rules on what a sale can
/// save are the database's (DATA-11); this checks them first, so the clerk's slips come back as results
/// (NET-12).
/// </summary>
public sealed partial class CheckoutCounter(
    IDbContextFactory<RewindDb> dbFactory,
    IMemberRoster roster,
    ILateFeeDraws draws,
    IStorePrices prices,
    TimeProvider time,
    ILogger<CheckoutCounter> logger) : ICheckoutCounter
{
    private const string LateFee = "Late fee";

    /// <inheritdoc />
    public async Task<SaleStart> StartAsync(string cardBarcode, CancellationToken cancellationToken)
    {
        var member = await roster.FindByCardAsync(cardBarcode, cancellationToken);
        if (member is null)
        {
            return new SaleStart(SaleStartStatus.NoSuchMember, null);
        }

        if (!member.IsActive)
        {
            return new SaleStart(SaleStartStatus.MemberInactive, null); // DrawAsync doesn't check, and leaves this to its caller
        }

        var draw = await draws.DrawAsync(member.Id, cancellationToken);

        // NET-7: the draw has committed, so a cancel from here on would only turn a done outcome into an
        // exception. These reads finish it.
        var now = await prices.GetAsync(CancellationToken.None);
        var fee = draw.Card == Card.Overdue ? await FeeDueAsync(draw, now.LateFeeCents) : null;
        return new SaleStart(SaleStartStatus.Started, new OpenedSale(member.Name, draw, now.RentalPriceCents, fee));
    }

    /// <inheritdoc />
    public async Task<CaseScan> ScanCaseAsync(string storeLabel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storeLabel);

        var label = Barcodes.Classify(storeLabel);
        if (label.Kind != ScanKind.StoreLabel)
        {
            return new CaseScan(CaseScanStatus.NoSuchCase, null);
        }

        // DATA-12: read untracked. NOCASE, as the other lists are, then the year, since remakes share titles.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var found = await db.Cases
            .AsNoTracking()
            .Where(c => c.StoreLabel == label.Code)
            .Select(c => new
            {
                c.Id,
                c.Status,
                Movies = c.Movies
                    .OrderBy(m => EF.Functions.Collate(m.Title, "NOCASE"))
                    .ThenBy(m => m.Year)
                    .Select(m => new MovieInCase(m.Id, m.Title, m.Year))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            return new CaseScan(CaseScanStatus.NoSuchCase, null);
        }

        return found.Status == CaseStatus.OnShelf
            ? new CaseScan(CaseScanStatus.Found, new CaseToRent(found.Id, label.Code, found.Movies))
            : new CaseScan(CaseScanStatus.NotOnShelf, null);
    }

    /// <inheritdoc />
    public async Task<SaleResult> CompleteAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var total = ticket.TotalCents;
        if (ticket.Items.Count == 0 && ticket.Lines.Count == 0)
        {
            return new SaleResult(SaleStatus.EmptyTicket, total, null, null);
        }

        if (ticket.CashTenderedCents < total)
        {
            return new SaleResult(SaleStatus.CashShort, total, null, null);
        }

        // ADR-0006: EF Core begins a transaction on SQLite as BEGIN IMMEDIATE, so it holds the write lock from
        // before this read, and nothing can take a case off the shelf between the look and the save.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var caseIds = ticket.Items.Select(item => item.CaseId).ToList();
        var cases = await db.Cases.Where(c => caseIds.Contains(c.Id)).ToListAsync(cancellationToken);
        if (cases.Find(c => c.Status != CaseStatus.OnShelf) is { } gone)
        {
            return new SaleResult(SaleStatus.NotOnShelf, total, gone.StoreLabel, null);
        }

        cases.ForEach(c => c.Status = CaseStatus.CheckedOut);
        var rental = NewRental(ticket, total);
        db.Rentals.Add(rental);
        await db.SaveChangesAsync(cancellationToken); // DATA-7: the rental, its items and lines, and each case's status
        await transaction.CommitAsync(cancellationToken);

        LogCompleted(logger, rental.Id, ticket.MemberId, ticket.BusinessDayId, ticket.Items.Count, ticket.Lines.Count, total, ticket.CashTenderedCents, rental.ChangeCents);
        var sale = new CompletedSale(
            rental.Id,
            total,
            ticket.CashTenderedCents,
            rental.ChangeCents,
            Change.Break(rental.ChangeCents),
            Change.CountBack(total, ticket.CashTenderedCents));
        return new SaleResult(SaleStatus.Completed, total, null, sale);
    }

    private Rental NewRental(Ticket ticket, int total)
    {
        var rental = new Rental
        {
            MemberId = ticket.MemberId,
            BusinessDayId = ticket.BusinessDayId,
            RentedAt = time.GetUtcNow().UtcDateTime, // NET-8, DATA-8
            TotalCents = total,
            CashTenderedCents = ticket.CashTenderedCents,
            ChangeCents = ticket.CashTenderedCents - total,
        };
        foreach (var item in ticket.Items)
        {
            rental.Items.Add(new RentalItem { CaseId = item.CaseId, MovieId = item.MovieId, PriceCents = item.PriceCents });
        }

        foreach (var line in ticket.Lines)
        {
            rental.Lines.Add(new SaleLine
            {
                Name = line.Name,
                PriceCents = line.PriceCents,
                ConcessionItemId = line.ConcessionItemId,
                LateFeeDrawId = line.LateFeeDrawId,
            });
        }

        return rental;
    }

    // ADR-0010: a fee line is what says a draw's fee is paid, so one that has a line has nothing left to charge.
    private async Task<FeeDue?> FeeDueAsync(LateFeeDrawRow draw, int lateFeeCents)
    {
        await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
        if (await db.SaleLines.AsNoTracking().AnyAsync(l => l.LateFeeDrawId == draw.Id, CancellationToken.None))
        {
            return null;
        }

        var movie = await db.Movies
            .AsNoTracking()
            .Where(m => m.Id == draw.FlavorMovieId)
            .Select(m => new { m.Title, m.Year })
            .SingleOrDefaultAsync(CancellationToken.None);
        return movie is null
            ? new FeeDue(LateFee, lateFeeCents, null, null)
            : new FeeDue($"{LateFee}: {Movie.TitleWithYear(movie.Title, movie.Year)}", lateFeeCents, movie.Title, movie.Year);
    }

    // Information (OBS-3): a checkout is what the night's log is about.
    [LoggerMessage(Level = LogLevel.Information, Message = "Completed rental {RentalId} for member {MemberId} on business day {BusinessDayId}: items {Items}, lines {Lines}, total {TotalCents}, cash {CashTenderedCents}, change {ChangeCents}")]
    private static partial void LogCompleted(ILogger logger, int rentalId, int memberId, int businessDayId, int items, int lines, int totalCents, int cashTenderedCents, int changeCents);
}
