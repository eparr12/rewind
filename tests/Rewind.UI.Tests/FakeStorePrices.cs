using Rewind.Core.Money;
using Rewind.Core.StoreManager;

namespace Rewind.UI.Tests;

/// <summary>
/// The prices' Core service as the Prices tab's tests set it up (ADR-0003). It holds the prices and answers
/// each save as Core does, reading what was typed with Dollars, unless the test gives an answer of its own,
/// which can be a task that hasn't finished. It records each save it's asked for.
/// </summary>
internal sealed class FakeStorePrices : IStorePrices
{
    private readonly List<(string RentalPrice, string LateFee)> _saves = [];

    /// <summary>The prices as Core holds them: the opening prices, until a save changes them.</summary>
    public Prices Current { get; private set; } = new(200, 100);

    /// <summary>What a read answers instead of <see cref="Current"/>, such as a read that hasn't finished.</summary>
    public Task<Prices>? Reading { get; set; }

    /// <summary>What a save answers instead of saving, such as a save that hasn't finished.</summary>
    public Task<PriceChange>? Saving { get; set; }

    /// <summary>Each save asked for, as typed, oldest first.</summary>
    public IReadOnlyList<(string RentalPrice, string LateFee)> Saves => _saves;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken { get; private set; }

    public Task<Prices> GetAsync(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        return Reading ?? Task.FromResult(Current);
    }

    public Task<PriceChange> SaveAsync(string rentalPrice, string lateFee, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _saves.Add((rentalPrice, lateFee));
        return Saving ?? Task.FromResult(Save(rentalPrice, lateFee));
    }

    private PriceChange Save(string rentalPrice, string lateFee)
    {
        if (!IsPrice(rentalPrice, out var rentalPriceCents))
        {
            return new PriceChange(PriceChangeStatus.RentalPriceNotValid, null);
        }

        if (!IsPrice(lateFee, out var lateFeeCents))
        {
            return new PriceChange(PriceChangeStatus.LateFeeNotValid, null);
        }

        Current = new Prices(rentalPriceCents, lateFeeCents);
        return new PriceChange(PriceChangeStatus.Saved, Current);
    }

    private static bool IsPrice(string typed, out int cents) =>
        Dollars.TryParse(typed, out cents) && cents <= StoreSettings.MaxPriceCents;
}
