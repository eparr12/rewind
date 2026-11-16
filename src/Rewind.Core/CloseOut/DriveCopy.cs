namespace Rewind.Core.CloseOut;

/// <summary>What copying the backups to the backup drive came to.</summary>
/// <param name="Status">Whether each backup drive has every backup now, and if not, why.</param>
/// <param name="Files">How many backups were copied, with one copied to two drives counted twice.</param>
/// <param name="Drives">
/// Each backup drive found, by its top folder, such as E:\. Empty for <see cref="DriveCopyStatus.NoDrive"/>.
/// </param>
/// <param name="Unwritable">
/// Each of those that couldn't be written to. Empty unless <paramref name="Status"/> is
/// <see cref="DriveCopyStatus.Unwritable"/>.
/// </param>
public sealed record DriveCopy(DriveCopyStatus Status, int Files, IReadOnlyList<string> Drives, IReadOnlyList<string> Unwritable);
