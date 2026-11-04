using Rewind.Core.Checkout;
using Rewind.Core.Money;

namespace Rewind.UI.Tests;

/// <summary>
/// The checkout counter's Core service as the Checkout screen's tests set it up (ADR-0003). It holds the cards
/// and cases a test gives it and answers each call as Core does, unless the test gives an answer of its own,
/// which can be a task that hasn't finished. It keeps each ticket it's given, as the screen built it.
/// </summary>
internal sealed class FakeCheckoutCounter : ICheckoutCounter
{
    private readonly List<string> _cards = [];
    private readonly List<string> _labels = [];
    private readonly List<Ticket> _tickets = [];

    /// <summary>The sale each card opens. Any other card is nobody's.</summary>
    public Dictionary<string, OpenedSale> Sales { get; } = [];

    /// <summary>The case on the shelf with each label. Any other label is no case's.</summary>
    public Dictionary<string, CaseToRent> Cases { get; } = [];

    /// <summary>What a card answers instead of looking in <see cref="Sales"/>, such as an inactive member.</summary>
    public Task<SaleStart>? Starting { get; set; }

    /// <summary>What a label answers instead of looking in <see cref="Cases"/>, such as a case off the shelf.</summary>
    public Task<CaseScan>? Scanning { get; set; }

    /// <summary>What completing a sale answers instead of working it out, such as a case that left the shelf.</summary>
    public Task<SaleResult>? Completing { get; set; }

    /// <summary>Each card asked about, oldest first.</summary>
    public IReadOnlyList<string> Cards => _cards;

    /// <summary>Each label asked about, oldest first.</summary>
    public IReadOnlyList<string> Labels => _labels;

    /// <summary>Each ticket the screen sent, oldest first.</summary>
    public IReadOnlyList<Ticket> Tickets => _tickets;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken { get; private set; }

    public Task<SaleStart> StartAsync(string cardBarcode, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _cards.Add(cardBarcode);
        return Starting ?? Task.FromResult(Sales.TryGetValue(cardBarcode, out var sale)
            ? new SaleStart(SaleStartStatus.Started, sale)
            : new SaleStart(SaleStartStatus.NoSuchMember, null));
    }

    public Task<CaseScan> ScanCaseAsync(string storeLabel, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _labels.Add(storeLabel);
        return Scanning ?? Task.FromResult(Cases.TryGetValue(storeLabel, out var found)
            ? new CaseScan(CaseScanStatus.Found, found)
            : new CaseScan(CaseScanStatus.NoSuchCase, null));
    }

    public Task<SaleResult> CompleteAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _tickets.Add(ticket);
        return Completing ?? Task.FromResult(Answer(ticket));
    }

    private SaleResult Answer(Ticket ticket)
    {
        var total = ticket.TotalCents;
        if (ticket.CashTenderedCents < total)
        {
            return new SaleResult(SaleStatus.CashShort, total, null, null);
        }

        var change = ticket.CashTenderedCents - total;
        return new SaleResult(
            SaleStatus.Completed,
            total,
            null,
            new CompletedSale(_tickets.Count, total, ticket.CashTenderedCents, change, Change.Break(change), Change.CountBack(total, ticket.CashTenderedCents)));
    }
}
