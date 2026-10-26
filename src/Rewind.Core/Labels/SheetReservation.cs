namespace Rewind.Core.Labels;

/// <summary>What making a sheet came to: the sheet, or why there's none.</summary>
/// <param name="Status">Whether the sheet's numbers were spent.</param>
/// <param name="Sheet">The sheet, ready to print. Null unless the status is <see cref="SheetStatus.Reserved"/>.</param>
public sealed record SheetReservation(SheetStatus Status, Sheet? Sheet);
