namespace Rewind.Core.Catalog;

/// <summary>
/// One physical case on the shelf, with a store label stuck on it. It holds one movie, or several
/// for a box set or a double feature. Cases are retired, never deleted, so their history stays
/// (DATA-4).
/// </summary>
public sealed class Case
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The store label on the case, such as RV-00042. No two cases share one (DATA-11).</summary>
    public required string StoreLabel { get; set; }

    /// <summary>
    /// The barcode the case came with, as 12 or 13 digits. It names an edition, so copies of one
    /// edition share it. Null when the case has none.
    /// </summary>
    public string? FactoryUpc { get; set; }

    /// <summary>Whether the case is on the shelf, checked out or retired. A new case goes on the shelf.</summary>
    public CaseStatus Status { get; set; } = CaseStatus.OnShelf;

    /// <summary>The movies in the case.</summary>
    public ICollection<Movie> Movies { get; } = [];
}
