namespace Rewind.Core.Labels;

/// <summary>One label's or one card's barcode on a sheet.</summary>
/// <param name="Code">What it holds, such as RV-00042, which the sheet prints as text too.</param>
/// <param name="Svg">The barcode as <see cref="Code128.Svg"/> drew it, at its kind's printed size.</param>
public sealed record SheetBarcode(string Code, string Svg);
