namespace Rewind.Core.LateFees;

/// <summary>
/// Gives a member their late-fee card for the night, as their card is scanned. <see cref="LateFeeDraws"/> does
/// it.
/// </summary>
public interface ILateFeeDraws
{
    /// <summary>
    /// The member's card for tonight, opening the night if none is open, since the first member card scanned
    /// at Checkout opens it. The member's first scan of the night draws from their bag and saves the draw with
    /// what's left of the bag. A rescan returns that draw unchanged, so it can't fish for a Clear (DATA-11). An
    /// Overdue card names a movie picked from those the member has watched: any movie the watch log says they
    /// watched, with stars or without, each counted once however often they've watched it. A member who hasn't
    /// watched anything yet gets one from the whole collection, and an empty collection names none. It doesn't
    /// check that the member is active, so the caller refuses an inactive card first.
    /// </summary>
    Task<LateFeeDrawRow> DrawAsync(int memberId, CancellationToken cancellationToken);
}
