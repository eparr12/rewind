namespace Rewind.Core.Members;

/// <summary>A member as the Store Manager shows them.</summary>
/// <param name="Id">The database key, which the Store Manager hands back to rename, deactivate or reactivate the member.</param>
/// <param name="Name">The name the register shows, such as Dad.</param>
/// <param name="CardBarcode">The barcode on the member's card, such as MEM-001.</param>
/// <param name="IsActive">False once the member is deactivated.</param>
public sealed record MemberRow(int Id, string Name, string CardBarcode, bool IsActive);
