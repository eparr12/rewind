namespace Rewind.Core.Members;

/// <summary>
/// A family member with a membership card. Members are deactivated, never deleted, so
/// their history stays (DATA-4).
/// </summary>
public sealed class Member
{
    /// <summary>The longest name a member can have: room for a name such as Grandpa Joe, not a sentence.</summary>
    public const int MaxNameLength = 30;

    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The name the register shows, such as Dad, no longer than <see cref="MaxNameLength"/>.</summary>
    public required string Name { get; set; }

    /// <summary>The barcode on the member's card, such as MEM-001. No two members share one.</summary>
    public required string CardBarcode { get; set; }

    /// <summary>False once the member is deactivated in the Store Manager.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// What's left of the member's late-fee cards, in draw order, such as CCO (<see cref="LateFees.ShuffleBag"/>).
    /// Empty when the bag is spent, which is how every member starts.
    /// </summary>
    public string Bag { get; set; } = "";
}
