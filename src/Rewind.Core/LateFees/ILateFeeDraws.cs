namespace Rewind.Core.LateFees;

/// <summary>
/// Gives a member their late-fee card for the night, as their card is scanned. <see cref="LateFeeDraws"/> does
/// it.
/// </summary>
public interface ILateFeeDraws
{
    /// <summary>
    /// The member's card for tonight, opening the night if this is its first scan. The first scan of the night
    /// draws from the member's bag and saves the draw with what's left of the bag. A rescan returns that draw
    /// unchanged, so it can't fish for a Clear (DATA-11). An Overdue card names a movie from the whole
    /// collection, or none when the collection is empty. It doesn't check that the member is active, so the
    /// caller refuses an inactive card first.
    /// </summary>
    Task<LateFeeDrawRow> DrawAsync(int memberId, CancellationToken cancellationToken);
}
