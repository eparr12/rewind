namespace Rewind.Core.Labels;

/// <summary>One snack's line on the concession menu.</summary>
/// <param name="Name">The snack's name, such as Popcorn.</param>
/// <param name="PriceCents">Its price in cents, such as 100 for $1.00.</param>
/// <param name="Barcode">Its code and its barcode, drawn at the menu's size.</param>
public sealed record MenuLine(string Name, int PriceCents, SheetBarcode Barcode);
