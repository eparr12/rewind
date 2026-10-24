namespace Rewind.Core.Metadata;

/// <summary>
/// UPCitemdb's answer for one factory barcode, saved so the free plan's 100 lookups a day go to
/// barcodes Rewind hasn't seen. Not found is an answer too, and it's saved the same way. Too fast
/// or no answer in time isn't an answer, so it's never saved, and the next scan asks again.
/// </summary>
public sealed class UpcLookup
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The barcode, as 12 or 13 digits. Each one is looked up once.</summary>
    public required string Upc { get; set; }

    /// <summary>
    /// The product's title as UPCitemdb gave it, such as "Halloween (Widescreen DVD)". Null when
    /// UPCitemdb doesn't know the barcode.
    /// </summary>
    public string? ProductTitle { get; set; }

    /// <summary>
    /// When UPCitemdb answered, in UTC (DATA-8). UPCitemdb keeps growing, so an old not-found
    /// answer may be worth asking again someday.
    /// </summary>
    public required DateTime LookedUpAt { get; set; }
}
