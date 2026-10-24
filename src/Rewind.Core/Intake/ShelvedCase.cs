namespace Rewind.Core.Intake;

/// <summary>A case that's just gone on the shelf, as the Intake screen says so.</summary>
/// <param name="StoreLabel">Its store label, such as RV-00042.</param>
/// <param name="Movies">Its movies, in the order they were added, each once.</param>
public sealed record ShelvedCase(string StoreLabel, IReadOnlyList<ShelvedMovie> Movies);
