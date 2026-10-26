namespace Rewind.Core.Labels;

/// <summary>
/// A sheet of store labels or member cards, numbered and ready to print. Its numbers were spent when it
/// was made, so printing it again, such as after a jam, prints the same numbers (ADR-0006).
/// </summary>
/// <param name="Kind">Store labels or member cards.</param>
/// <param name="StoreName">The store's name, from StoreSettings, for each label and card.</param>
/// <param name="Barcodes">Each label's or card's barcode, in number order.</param>
public sealed record Sheet(SheetKind Kind, string StoreName, IReadOnlyList<SheetBarcode> Barcodes)
{
    /// <summary>The sheet's first code, such as RV-00031.</summary>
    public string First => Barcodes[0].Code;

    /// <summary>The sheet's last code, such as RV-00060.</summary>
    public string Last => Barcodes[^1].Code;
}
