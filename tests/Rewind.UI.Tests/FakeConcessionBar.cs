using Rewind.Core.Concessions;
using Rewind.Core.Money;
using Rewind.Core.StoreManager;

namespace Rewind.UI.Tests;

/// <summary>
/// The concession bar's Core service as the Snacks tab's tests set it up (ADR-0003). It holds snacks as Core
/// would and answers each call as Core does, reading prices with Dollars to the same limit, unless the test
/// gives an answer of its own, which can be a task that hasn't finished. It records each change it's asked
/// for, as typed.
/// </summary>
internal sealed class FakeConcessionBar : IConcessionBar
{
    private readonly List<string> _calls = [];
    private readonly List<CancellationToken> _tokens = [];

    /// <summary>The snacks as Core holds them. A change the fake answers as Core would changes them too.</summary>
    public List<ConcessionRow> Rows { get; } = [];

    /// <summary>What a read of the snacks answers instead of <see cref="Rows"/>, such as a read that hasn't finished.</summary>
    public Task<IReadOnlyList<ConcessionRow>>? Listing { get; set; }

    /// <summary>What an add answers instead of saving, such as a code in use.</summary>
    public Task<ConcessionChange>? Adding { get; set; }

    /// <summary>What a rename answers instead of saving, such as a blank name.</summary>
    public Task<ConcessionChange>? Renaming { get; set; }

    /// <summary>What a price change answers instead of saving, such as a save that hasn't finished.</summary>
    public Task<ConcessionChange>? Repricing { get; set; }

    /// <summary>Each change asked for, oldest first, as typed, such as "Add Popcorn CON-POPCORN 1.00".</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>The token each call was given, the read's included, oldest first.</summary>
    public IReadOnlyList<CancellationToken> Tokens => _tokens;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken => _tokens[^1];

    public Task<IReadOnlyList<ConcessionRow>> ListAsync(CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        return Listing ?? Task.FromResult<IReadOnlyList<ConcessionRow>>([.. Rows]);
    }

    public Task<ConcessionChange> AddAsync(string name, string code, string price, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"Add {name} {code} {price}");
        return Adding ?? SaveAtPrice(price, cents => new ConcessionRow(Rows.Count + 1, name, code, cents, IsActive: true));
    }

    public Task<ConcessionChange> RenameAsync(int itemId, string name, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"Rename {itemId} {name}");
        return Renaming ?? Saved(Row(itemId) with { Name = name });
    }

    public Task<ConcessionChange> SetPriceAsync(int itemId, string price, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"SetPrice {itemId} {price}");
        return Repricing ?? SaveAtPrice(price, cents => Row(itemId) with { PriceCents = cents });
    }

    public Task<ConcessionChange> SetActiveAsync(int itemId, bool isActive, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"SetActive {itemId} {isActive}");
        return Saved(Row(itemId) with { IsActive = isActive });
    }

    // As Core reads a price the Store Manager typed: in dollars, up to the store's limit, or refused.
    private Task<ConcessionChange> SaveAtPrice(string price, Func<int, ConcessionRow> item) =>
        Dollars.TryParse(price, out var cents) && cents <= StoreSettings.MaxPriceCents
            ? Saved(item(cents))
            : Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NotAPrice, null));

    private ConcessionRow Row(int itemId) => Rows.Single(row => row.Id == itemId);

    private Task<ConcessionChange> Saved(ConcessionRow item)
    {
        Rows.RemoveAll(row => row.Id == item.Id);
        Rows.Add(item);
        return Task.FromResult(new ConcessionChange(ConcessionChangeStatus.Saved, item));
    }
}
