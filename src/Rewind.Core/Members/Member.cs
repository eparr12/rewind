namespace Rewind.Core.Members;

/// <summary>
/// A family member with a membership card. Members are deactivated, never deleted, so
/// their history stays (DATA-4).
/// </summary>
public sealed class Member
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The name the register shows, such as Dad.</summary>
    public required string Name { get; set; }

    /// <summary>The barcode on the member's card, such as MEM-001. No two members share one.</summary>
    public required string CardBarcode { get; set; }

    /// <summary>False once the member is deactivated in the Store Manager.</summary>
    public bool IsActive { get; set; } = true;
}
