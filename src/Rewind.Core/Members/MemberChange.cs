namespace Rewind.Core.Members;

/// <summary>What a change to the members came to.</summary>
/// <param name="Status">Whether it was saved, and if not, what to fix.</param>
/// <param name="Member">
/// The member as saved, or, for <see cref="MemberChangeStatus.CardInUse"/>, the member who has the card.
/// Null otherwise.
/// </param>
public sealed record MemberChange(MemberChangeStatus Status, MemberRow? Member);
