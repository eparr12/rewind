namespace Rewind.Core.Members;

/// <summary>
/// What the Store Manager asks of Core to keep the family's members, one call per action (ARCH-5): the
/// list, and adding, renaming, deactivating and reactivating a member. Nothing deletes a member (DATA-4),
/// and each card belongs to one member (DATA-11). <see cref="MemberRoster"/> does it, and the screen's
/// tests use a fake (ADR-0003).
/// </summary>
public interface IMemberRoster
{
    /// <summary>Every member, active or not, in order of name, then card.</summary>
    Task<IReadOnlyList<MemberRow>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds an active member with the card that was scanned. The name is cleaned as a title typed into
    /// Intake is, and cut at <see cref="Member.MaxNameLength"/> characters. A card another member has,
    /// active or not, comes back <see cref="MemberChangeStatus.CardInUse"/>, with that member.
    /// </summary>
    Task<MemberChange> AddAsync(string name, string cardBarcode, CancellationToken cancellationToken);

    /// <summary>
    /// Fixes a member's name, cleaned as <see cref="AddAsync"/> cleans it. Renaming is for typos: someone
    /// new is a new member, with a card of their own. A name that's the same once cleaned changes nothing,
    /// and nothing is logged.
    /// </summary>
    Task<MemberChange> RenameAsync(int memberId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates a member, or reactivates one. A deactivated member keeps their card and their history
    /// (DATA-4). Asking for the state a member is in already changes nothing, and nothing is logged.
    /// </summary>
    Task<MemberChange> SetActiveAsync(int memberId, bool isActive, CancellationToken cancellationToken);
}
