namespace Rewind.Core.Labels;

/// <summary>
/// What the Store Manager asks of Core to print store labels, member cards and the concession menu, one
/// call per action (ARCH-5): the codes the next sheets start at, a sheet of labels or cards, numbered and
/// drawn, and the menu, drawn. A sheet's numbers are spent when it's made, before it's printed, so no two
/// labels or cards ever share a number (ADR-0006); the menu has none to spend. <see cref="LabelSheets"/>
/// does it, and the screen's tests use a fake (ADR-0003).
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

    /// <summary>
    /// Draws the concession menu: the store's name, its rental price and its late fee, then each snack on
    /// sale, in order of name, then code, with its price and its barcode, dated today on the register's
    /// calendar. Every snack on sale is on it, however many: a long menu goes on to a second sheet as it
    /// prints. With no snack on sale, it comes back <see cref="MenuStatus.NothingOnSale"/>.
    /// </summary>
    Task<MenuDrawing> DrawMenuAsync(CancellationToken cancellationToken);
}
