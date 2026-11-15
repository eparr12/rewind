namespace Rewind.Core.Returns;

/// <summary>What a returned case's scan came to.</summary>
/// <param name="Status">Whether the case was checked in, and if not, why.</param>
/// <param name="Rental">
/// The rental to ask who watched: the one just checked in, or, for a case that's on the shelf already, its last
/// rental while that rental's watch log is empty (ADR-0012). Null for any other outcome.
/// </param>
public sealed record CheckIn(CheckInStatus Status, ReturnedRental? Rental);
