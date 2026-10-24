namespace Rewind.Core.Catalog;

/// <summary>
/// Where a case is. The database stores the name, such as OnShelf, so the file reads plainly in
/// any tool.
/// </summary>
public enum CaseStatus
{
    /// <summary>On the shelf, ready to rent.</summary>
    OnShelf,

    /// <summary>Rented and not back yet.</summary>
    CheckedOut,

    /// <summary>Gone from the shelves for good, such as with a broken disc. Its history stays (DATA-4).</summary>
    Retired,
}
