namespace Rewind.Core.Labels;

/// <summary>
/// What the Store Manager asks of Core to print store labels and member cards, one call per action
/// (ARCH-5): the codes the next sheets start at, and a sheet of either kind, numbered and drawn. A
/// sheet's numbers are spent when it's made, before it's printed, so no two labels or cards ever share
/// a number (ADR-0006). <see cref="LabelSheets"/> does it, and the screen's tests use a fake (ADR-0003).
/// </summary>
public interface ILabelSheets
{
    /// <summary>The codes the next sheets of store labels and member cards start at.</summary>
    Task<NextCodes> PeekAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Spends the next <see cref="LabelSheets.LabelsPerSheet"/> store label numbers and draws a sheet of
    /// them. A sheet that would pass RV-99999 comes back <see cref="SheetStatus.OutOfNumbers"/>, and
    /// nothing is spent.
    /// </summary>
    Task<SheetReservation> ReserveLabelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Spends the next <see cref="LabelSheets.CardsPerSheet"/> member card numbers and draws a sheet of
    /// them. A sheet that would pass MEM-999 comes back <see cref="SheetStatus.OutOfNumbers"/>, and
    /// nothing is spent.
    /// </summary>
    Task<SheetReservation> ReserveCardsAsync(CancellationToken cancellationToken);
}
