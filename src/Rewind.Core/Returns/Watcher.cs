namespace Rewind.Core.Returns;

/// <summary>A member who watched a returned movie, with their stars.</summary>
/// <param name="MemberId">The member, from <see cref="ReturnedRental.People"/>.</param>
/// <param name="Stars">
/// From <see cref="Viewing.MinStars"/> to <see cref="Viewing.MaxStars"/>, or null when they watched it and gave no
/// stars (DATA-3).
/// </param>
public sealed record Watcher(int MemberId, int? Stars);
