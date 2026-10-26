namespace Rewind.Core.Labels;

/// <summary>The codes the next sheets of store labels and member cards start at.</summary>
/// <param name="StoreLabel">The next sheet of store labels' first code, such as RV-00031.</param>
/// <param name="MemberCard">The next sheet of member cards' first code, such as MEM-001.</param>
public sealed record NextCodes(string StoreLabel, string MemberCard);
