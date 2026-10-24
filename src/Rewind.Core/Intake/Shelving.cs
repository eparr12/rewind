namespace Rewind.Core.Intake;

/// <summary>What a case's label scan came to: the case on the shelf, or why it isn't.</summary>
/// <param name="Status">Whether the case was saved, and if not, why.</param>
/// <param name="Case">The case on the shelf. Null unless the status is <see cref="ShelveStatus.Shelved"/>.</param>
public sealed record Shelving(ShelveStatus Status, ShelvedCase? Case);
