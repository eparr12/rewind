namespace Rewind.Core.Metadata;

/// <summary>
/// Whether a factory barcode gave a title, and if not, why, so the screen can tell the clerk. A
/// failed lookup is an outcome, never an exception (NET-12).
/// </summary>
public enum UpcStatus
{
    /// <summary>UPCitemdb named the product.</summary>
    Found,

    /// <summary>
    /// UPCitemdb said, in its own words, that it doesn't know the barcode, or the code isn't a
    /// factory barcode at all. That's an answer too, so it's saved, and the barcode isn't asked
    /// about again for a month.
    /// </summary>
    NotFound,

    /// <summary>
    /// UPCitemdb couldn't answer this time; the reason says why. Nothing is saved, so the next scan
    /// of the barcode asks again.
    /// </summary>
    TryLater,
}
