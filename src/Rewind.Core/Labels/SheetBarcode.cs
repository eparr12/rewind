namespace Rewind.Core.Labels;

/// <summary>One barcode on a printed sheet: a label's, a card's, or a snack's on the concession menu.</summary>
/// <param name="Code">What it holds, such as RV-00042, which the sheet prints as text too.</param>
/// <param name="Svg">The barcode as <see cref="Code128.Svg"/> drew it, at its kind's printed size.</param>
public sealed record SheetBarcode(string Code, string Svg);
