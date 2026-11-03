namespace Rewind.Core.LateFees;

/// <summary>A card from a member's late-fee bag (<see cref="ShuffleBag"/>).</summary>
public enum Card
{
    /// <summary>Nothing's overdue tonight.</summary>
    Clear,

    /// <summary>A made-up overdue rental, whose late fee goes on the bill.</summary>
    Overdue,
}
