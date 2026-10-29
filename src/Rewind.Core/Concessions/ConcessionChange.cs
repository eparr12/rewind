namespace Rewind.Core.Concessions;

/// <summary>What a change to the concession items came to.</summary>
/// <param name="Status">Whether it was saved, and if not, what to fix.</param>
/// <param name="Item">
/// The item as saved, or, for <see cref="ConcessionChangeStatus.CodeInUse"/>, the item that has the code.
/// Null otherwise.
/// </param>
public sealed record ConcessionChange(ConcessionChangeStatus Status, ConcessionRow? Item);
