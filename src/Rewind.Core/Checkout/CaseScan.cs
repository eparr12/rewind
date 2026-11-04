namespace Rewind.Core.Checkout;

/// <summary>What a scanned store label came to.</summary>
/// <param name="Status">Whether it's a case that can go on the bill, and if not, why.</param>
/// <param name="Case">The case. Null unless <paramref name="Status"/> is <see cref="CaseScanStatus.Found"/>.</param>
public sealed record CaseScan(CaseScanStatus Status, CaseToRent? Case);
